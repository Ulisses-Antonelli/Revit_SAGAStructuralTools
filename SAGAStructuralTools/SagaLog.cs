using System;
using System.IO;
using System.Reflection;

namespace SAGAStructuralTools
{
    internal static class SagaLog
    {
        private static readonly string _path = ResolvePath();

        private static string ResolvePath()
        {
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dir))
                    return Path.Combine(dir, "SAGA_Debug.txt");
            }
            catch { }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "SAGA_Debug.txt");
        }

        public static void Write(string msg)
        {
            try { File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n"); }
            catch { }
        }

        public static void Exception(string context, Exception ex)
        {
            Write($"ERRO [{context}]: {ex.GetType().Name}: {ex.Message}");
            if (ex.StackTrace != null)
                Write($"  Stack: {ex.StackTrace.Replace("\r\n", " | ").Replace("\n", " | ")}");
            if (ex.InnerException != null)
                Write($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }
    }
}
