using System;
using System.IO;
using System.Reflection;

namespace SAGAStructuralTools.Core.Mapping
{
    /// <summary>
    /// Acha a pasta do catálogo Gerdau (.rfa) já configurada. Primeiro tenta reaproveitar a mesma
    /// pasta já escolhida na ferramenta "Converter IFC" (MainViewModel salva em
    /// SAGAStructuralTools.settings, primeira linha) — evita pedir a mesma pasta duas vezes pra
    /// ferramentas diferentes. Se não achar, cai pro próprio arquivo dedicado desta ferramenta, que
    /// também guarda a pasta de logs (segunda linha).
    /// </summary>
    internal static class GerdauCatalogSettings
    {
        private static readonly string BaseDir =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

        private static readonly string SharedIfcSettingsFile = Path.Combine(BaseDir, "SAGAStructuralTools.settings");
        private static readonly string OwnSettingsFile = Path.Combine(BaseDir, "SAGA_RobotImportSettings.txt");

        public static string GetSavedFolder()
        {
            try
            {
                var lines = ReadOwnLines();
                if (lines.Length > 0 && Directory.Exists(lines[0].Trim())) return lines[0].Trim();

                if (File.Exists(SharedIfcSettingsFile))
                {
                    var sharedLines = File.ReadAllLines(SharedIfcSettingsFile);
                    if (sharedLines.Length > 0 && Directory.Exists(sharedLines[0].Trim())) return sharedLines[0].Trim();
                }
            }
            catch (Exception) { /* falha de I/O não é fatal — só pede a pasta de novo */ }
            return null;
        }

        public static string GetSavedLogDirectory()
        {
            try
            {
                var lines = ReadOwnLines();
                if (lines.Length > 1 && Directory.Exists(lines[1].Trim())) return lines[1].Trim();
            }
            catch (Exception) { /* falha de I/O não é fatal */ }
            return null;
        }

        public static void Save(string catalogFolder, string logDirectory)
        {
            try { File.WriteAllLines(OwnSettingsFile, new[] { catalogFolder ?? "", logDirectory ?? "" }); }
            catch (Exception) { /* falha de I/O é não-fatal aqui */ }
        }

        private static string[] ReadOwnLines()
            => File.Exists(OwnSettingsFile) ? File.ReadAllLines(OwnSettingsFile) : Array.Empty<string>();
    }
}
