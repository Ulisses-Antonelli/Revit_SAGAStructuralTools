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
            var text = Decode(bytes);

            text = text.TrimStart('\uFEFF');
            return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        private static string Decode(byte[] bytes)
        {
            // BOM UTF-16 LE/BE: a decodifica\u00E7\u00E3o estrita UTF-8 abaixo sempre falha
            // nesses bytes (0xFF/0xFE nunca s\u00E3o in\u00EDcio v\u00E1lido de UTF-8) e cairia no
            // fallback Latin-1, que leria cada caractere de 2 bytes como dois
            // caracteres separados (o real + um NUL invis\u00EDvel), quebrando as linhas.
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes);

            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Latin1.GetString(bytes);
            }
        }
    }
}
