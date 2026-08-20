using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Rail;
using System;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Handler de ExternalEvent para seleção interativa de vigas estruturais (e,
    /// opcionalmente, pilares — usado no pick de referência de deslocamento lateral).
    /// Captura tanto o ElementId quanto o ponto exato do clique (GlobalPoint),
    /// que é usado para posicionar a escada ao longo da viga.
    /// </summary>
    public class BeamPickHandler : IExternalEventHandler
    {
        private readonly ISelectionFilter _filter;
        private readonly string _prompt;

        public BeamPickHandler()
            : this(new StructuralFramingFilter(), "Selecione a viga estrutural (ESC para cancelar)")
        {
        }

        /// <summary>
        /// Construtor usado pelo pick de referência lateral, que também aceita pilares
        /// (reaproveita o mesmo filtro de viga/pilar reto usado nas ferramentas de canto).
        /// </summary>
        public BeamPickHandler(ISelectionFilter filter, string prompt)
        {
            _filter = filter;
            _prompt = prompt;
        }

        /// <summary>
        /// Disparado após seleção: (ElementId, Nome, PontoDoClique projetado no eixo da viga).
        /// </summary>
        public event Action<ElementId, string, XYZ> BeamPicked;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var sel   = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    _filter,
                    _prompt);

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

    /// <summary>
    /// Filtro do pick de referência lateral: viga reta OU pilar (reto ou por ponto) —
    /// mesmo critério já usado pelas ferramentas de canto arredondado.
    /// </summary>
    internal class BeamOrColumnFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => RoundedCornerMember.IsSelectable(elem);

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
