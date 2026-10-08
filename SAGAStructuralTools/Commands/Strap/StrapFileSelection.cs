using System;
using System.IO;

namespace SAGAStructuralTools.Commands.Strap
{
    internal enum StrapFileSelectionKind
    {
        Cancelled,
        Supported,
        LegacyDoc
    }

    internal static class StrapFileSelection
    {
        public static StrapFileSelectionKind Classify(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return StrapFileSelectionKind.Cancelled;

            return string.Equals(Path.GetExtension(path), ".doc",
                StringComparison.OrdinalIgnoreCase)
                ? StrapFileSelectionKind.LegacyDoc
                : StrapFileSelectionKind.Supported;
        }
    }
}
