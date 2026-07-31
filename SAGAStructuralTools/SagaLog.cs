using System;
using System.IO;
using System.Text;

namespace SAGAStructuralTools
{
    // Deliberadamente BCL-only: este tipo precisa funcionar antes da resolucao
    // de qualquer assembly da pilha STRAP.
    internal static class SagaLog
    {
        private const string ProductPath = "SAGA\\SAGAStructuralTools\\Logs";

        public static void Write(string message)
        {
            try
            {
                Append(ResolvePrimaryPath(), message);
                return;
            }
            catch
            {
                // O logger nunca pode propagar uma falha ao host Revit.
            }

            try
            {
                Append(ResolveFallbackPath(), message);
            }
            catch
            {
                // Ultimo recurso deliberadamente silencioso.
            }
        }

        public static void Exception(string context, Exception exception)
        {
            try
            {
                Write("ERRO [" + context + "]");
                WriteException(exception, 0);
            }
            catch
            {
                // Nem uma arvore de excecoes incomum pode escapar do logger.
            }
        }

        internal static string ResolvePrimaryPath()
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, ProductPath,
                "SAGAStructuralTools-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }

        internal static string ResolveFallbackPath()
        {
            return Path.Combine(Path.GetTempPath(), "SAGA", "SAGAStructuralTools",
                "Logs", "SAGAStructuralTools-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }

        private static void Append(string path, string message)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + "] " +
                (message ?? string.Empty) + Environment.NewLine;
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }

        private static void WriteException(Exception exception, int depth)
        {
            if (exception == null)
                return;

            string prefix = new string(' ', depth * 2);
            Write(prefix + exception.GetType().FullName + ": " + exception.Message);
            Write(prefix + "StackTrace: " + (exception.StackTrace ?? "<indisponivel>"));
            if (exception.InnerException != null)
            {
                Write(prefix + "InnerException:");
                WriteException(exception.InnerException, depth + 1);
            }
        }
    }
}
