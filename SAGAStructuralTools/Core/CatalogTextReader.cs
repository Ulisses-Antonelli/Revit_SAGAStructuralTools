using System;
using System.IO;
using System.Text;

namespace SAGAStructuralTools.Core
{
    /// <summary>
    /// Lê catálogos de tipos tanto em UTF-8 quanto no formato ANSI/Latin-1
    /// tradicionalmente exportado pelo Revit. Encoding.Default não é estável:
    /// no .NET Framework usa a página do Windows, mas no .NET 8 usa UTF-8.
    /// </summary>
    public static class CatalogTextReader
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public static string[] ReadAllLines(string path)
        {
            var bytes = File.ReadAllBytes(path);
            string text;

            try
            {
                text = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                text = Latin1.GetString(bytes);
            }

            text = text.TrimStart('\uFEFF');
            return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }
    }
}
