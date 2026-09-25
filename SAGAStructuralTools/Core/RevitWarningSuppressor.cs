using Autodesk.Revit.DB;

namespace SAGAStructuralTools.Core
{
    /// <summary>
    /// Suprime avisos (não erros) do Revit durante uma transação — ex.: "Beam or Brace is slightly
    /// off axis", "viga ligeiramente fora da linha central". Mesma técnica já usada no conversor de
    /// IFC (ElementConverter.SuppressRevitWarnings).
    /// </summary>
    internal sealed class RevitWarningSuppressor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (var msg in failuresAccessor.GetFailureMessages())
            {
                if (msg.GetSeverity() == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(msg);
            }
            return FailureProcessingResult.Continue;
        }
    }
}
