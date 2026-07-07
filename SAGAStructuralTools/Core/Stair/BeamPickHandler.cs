using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core;
using System;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Handler de ExternalEvent para seleção interativa de vigas estruturais.
    /// Captura tanto o ElementId quanto o ponto exato do clique (GlobalPoint),
    /// que é usado para posicionar a escada ao longo da viga.
    /// </summary>
    public class BeamPickHandler : IExternalEventHandler
    {
        /// <summary>
        /// Disparado após seleção: (ElementId, Nome, PontoDoClique projetado no eixo da viga).
        /// </summary>
        public event Action<ElementId, string, XYZ> BeamPicked;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc  = app.ActiveUIDocument;
                var filter = new StructuralFramingFilter();
                var sel    = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    filter,
                    "Selecione a viga estrutural (ESC para cancelar)");

                var element    = uidoc.Document.GetElement(sel.ElementId);
                var clickPoint = sel.GlobalPoint;

                // Projeta o ponto do clique no eixo da viga para posicionamento preciso
                var axisPoint = ProjectOntoBeamAxis(element, clickPoint);

                BeamPicked?.Invoke(sel.ElementId, element?.Name ?? sel.ElementId.ToString(), axisPoint);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // Usuário cancelou — nenhuma ação
            }
        }

        /// <summary>
        /// Projeta o ponto do clique no eixo (LocationCurve) da viga.
        /// Garante que o ponto de conexão da escada fique exatamente na linha da viga.
        /// </summary>
        private static XYZ ProjectOntoBeamAxis(Element beam, XYZ clickPoint)
        {
            if (!(beam.Location is LocationCurve lc)) return clickPoint;

            try
            {
                var result = lc.Curve.Project(clickPoint);
                return result?.XYZPoint ?? clickPoint;
            }
            catch
            {
                return clickPoint;
            }
        }

        public string GetName() => "SAGAPickBeam";
    }

    internal class StructuralFramingFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
            => elem?.Category?.Id.GetId() == (int)BuiltInCategory.OST_StructuralFraming;

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
