namespace SAGAStructuralTools.Strap.Readers
{
    public enum ReaderDiagnosticSeverity
    {
        Warning,
        Error
    }

    public static class ReaderDiagnosticCodes
    {
        public const string CorruptDocument = "CORRUPT_DOCUMENT";
        public const string EmptyFile = "EMPTY_FILE";
        public const string FileNotFound = "FILE_NOT_FOUND";
        public const string PdfInsufficientText = "PDF_INSUFFICIENT_TEXT";
        public const string PdfNoText = "PDF_NO_TEXT";
        public const string PdfUnrecognizableStructure = "PDF_UNRECOGNIZABLE_STRUCTURE";
        public const string UnsupportedExtension = "UNSUPPORTED_EXTENSION";
    }

    public sealed class ReaderDiagnostic
    {
        public ReaderDiagnostic(
            ReaderDiagnosticSeverity severity,
            string code,
            string message,
            bool requiresOcr = false,
            int? pageNumber = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            RequiresOcr = requiresOcr;
            PageNumber = pageNumber;
        }

        public ReaderDiagnosticSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public bool RequiresOcr { get; }
        public int? PageNumber { get; }
    }
}
