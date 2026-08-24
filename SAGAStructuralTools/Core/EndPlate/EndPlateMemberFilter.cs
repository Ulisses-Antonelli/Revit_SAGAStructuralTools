using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace SAGAStructuralTools.Core.EndPlate
{
    /// <summary>Aceita vigas (Structural Framing) e pilares (Structural Columns).</summary>
    internal sealed class EndPlateMemberFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            var catId = element?.Category?.Id.GetId();
            return catId == (int)BuiltInCategory.OST_StructuralFraming ||
                   catId == (int)BuiltInCategory.OST_StructuralColumns;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
