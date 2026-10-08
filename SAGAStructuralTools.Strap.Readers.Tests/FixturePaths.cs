using System.IO;

namespace SAGAStructuralTools.Strap.Readers.Tests
{
    internal static class FixturePaths
    {
        public static string Get(string fileName)
            => Path.Combine(
                System.AppContext.BaseDirectory,
                "Fixtures",
                fileName);
    }
}
