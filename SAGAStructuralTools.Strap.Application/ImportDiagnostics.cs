namespace SAGAStructuralTools.Strap.Application
{
    public enum DiagnosticSeverity
    {
        Warning,
        Error
    }

    public static class DiagnosticCodes
    {
        public const string AmbiguousValueCombination = "AMBIGUOUS_VALUE_COMBINATION";
        public const string ChangedHeaderInsideBlock = "CHANGED_HEADER_INSIDE_BLOCK";
        public const string DegradedLabelOutsideContext = "DEGRADED_LABEL_OUTSIDE_CONTEXT";
        public const string DuplicateNodeConflict = "DUPLICATE_NODE_CONFLICT";
        public const string DuplicateNodeIdentical = "DUPLICATE_NODE_IDENTICAL";
        public const string InconsistentUnit = "INCONSISTENT_UNIT";
        public const string MissingCombinationAfterMaximum = "MISSING_COMBINATION_AFTER_MAXIMUM";
        public const string MissingCombinationAfterMinimum = "MISSING_COMBINATION_AFTER_MINIMUM";
        public const string MissingCombination = "MISSING_COMBINATION";
        public const string MissingMaximum = "MISSING_MAXIMUM";
        public const string MissingMinimum = "MISSING_MINIMUM";
        public const string MissingNode = "MISSING_NODE";
        public const string MissingUnit = "MISSING_UNIT";
        public const string MinimumWithoutCurrentNode = "MINIMUM_WITHOUT_CURRENT_NODE";
        public const string NewNodeBeforeBlockCompleted = "NEW_NODE_BEFORE_BLOCK_COMPLETED";
        public const string UnexpectedCombinationCount = "UNEXPECTED_COMBINATION_COUNT";
        public const string UnexpectedEffortCount = "UNEXPECTED_EFFORT_COUNT";
        public const string UnrecognizedReactionRow = "UNRECOGNIZED_REACTION_ROW";
    }

    public sealed class ImportDiagnostic
    {
        public ImportDiagnostic(
            DiagnosticSeverity severity,
            string code,
            string message,
            SourceTrace trace = null,
            string nodeId = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            Trace = trace;
            NodeId = nodeId;
        }

        public DiagnosticSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public SourceTrace Trace { get; }
        public string NodeId { get; }
    }
}
