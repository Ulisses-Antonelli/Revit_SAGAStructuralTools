using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;

namespace SAGAStructuralTools.Core.Rail
{
    /// <summary>
    /// Seleção de UMA linha por vez (PickObject único), igual ao padrão do BeamPickHandler
    /// da escada. Retorna imediatamente após um clique — não deixa pick pendente, então o
    /// ExternalEvent de criação roda a qualquer momento. Para adicionar várias linhas, o
    /// usuário aciona a seleção repetidamente. Aceita apenas CurveElement (Linhas).
    /// </summary>
    public class LinePickHandler : IExternalEventHandler
    {
        /// <summary>Disparado após um clique válido: (ElementId, comprimento em mm).</summary>
        public event Action<ElementId, double> LinePicked;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var r     = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new CurveElementFilter(),
                    "Clique em UMA linha do perímetro (ESC para cancelar).");

                var el     = uidoc.Document.GetElement(r.ElementId);
                double len = GetLengthMm(el);
                if (len > 1.0)
                    LinePicked?.Invoke(r.ElementId, len);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // ESC cancela o clique — nenhuma ação.
            }
        }

        private static double GetLengthMm(Element el)
        {
            if (el?.Location is LocationCurve lc)
                return lc.Curve.Length * 304.8;

            var p = el?.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            return p != null ? p.AsDouble() * 304.8 : 0;
        }

        public string GetName() => "SAGAPickRailLine";
    }

    internal class CurveElementFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)  => elem is CurveElement;
        public bool AllowReference(Reference r, XYZ pos) => false;
    }
}
