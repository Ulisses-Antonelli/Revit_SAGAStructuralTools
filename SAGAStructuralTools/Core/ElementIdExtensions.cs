using Autodesk.Revit.DB;

namespace SAGAStructuralTools.Core
{
    // ElementId.IntegerValue (int) foi removido no Revit 2025+ e substituído por
    // ElementId.Value (long). Este helper abstrai a diferença entre os dois targets.
    internal static class ElementIdExtensions
    {
        internal static long GetId(this ElementId id)
        {
#if NET8_0_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }
    }
}
