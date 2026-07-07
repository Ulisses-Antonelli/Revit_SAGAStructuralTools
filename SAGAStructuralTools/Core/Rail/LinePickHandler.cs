using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Rail
{
    public class LinePickHandler : IExternalEventHandler
    {
        public event Action<List<(ElementId id, double lengthMm)>> LinesPicked;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var refs  = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new CurveElementFilter(),
                    "Selecione as linhas de perímetro e pressione Enter para confirmar");

                var result = new List<(ElementId, double)>();
                foreach (var r in refs)
                {
                    var el     = uidoc.Document.GetElement(r.ElementId);
                    double len = GetLengthMm(el);
                    if (len > 1.0)
                        result.Add((r.ElementId, len));
                }

                if (result.Count > 0)
                    LinesPicked?.Invoke(result);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
        }

        private static double GetLengthMm(Element el)
        {
            if (el?.Location is LocationCurve lc)
                return lc.Curve.Length * 304.8;

            var p = el?.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            return p != null ? p.AsDouble() * 304.8 : 0;
        }

        public string GetName() => "SAGAPickRailLines";
    }

    internal class CurveElementFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)  => elem is CurveElement;
        public bool AllowReference(Reference r, XYZ pos) => false;
    }
}
