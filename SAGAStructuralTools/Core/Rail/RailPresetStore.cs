using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SAGAStructuralTools.Core.Rail
{
    /// <summary>
    /// Persiste configurações de guarda-corpo (RailConfig) como presets nomeados em
    /// %APPDATA%\SAGA\RailPresets\*.saga-rail.xml (globais por usuário, valem em qualquer
    /// projeto). Também mantém a "última usada" para auto-carregar ao abrir a janela.
    ///
    /// Usa XmlSerializer (in-framework) para não exigir dependência externa e funcionar
    /// tanto em net48 (Revit 2023) quanto em net8 (Revit 2026).
    /// </summary>
    public static class RailPresetStore
    {
        private const string Ext      = ".saga-rail.xml";
        private const string LastName = "_ultimo";   // preset interno (não aparece na lista)

        private static readonly XmlSerializer Ser = new XmlSerializer(typeof(RailConfig));

        private static string Dir
        {
            get
            {
                var d = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SAGA", "RailPresets");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        /// <summary>Nomes dos presets salvos (exclui os internos com prefixo '_').</summary>
        public static List<string> List()
        {
            try
            {
                return Directory.GetFiles(Dir, "*" + Ext)
                    .Select(f => Path.GetFileName(f))
                    .Select(n => n.Substring(0, n.Length - Ext.Length))
                    .Where(n => !n.StartsWith("_"))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        public static void Save(string name, RailConfig config)
        {
            if (string.IsNullOrWhiteSpace(name) || config == null) return;
            using (var fs = File.Create(PathFor(name)))
                Ser.Serialize(fs, config);
        }

        public static RailConfig Load(string name)
        {
            try
            {
                var path = PathFor(name);
                if (!File.Exists(path)) return null;
                using (var fs = File.OpenRead(path))
                    return (RailConfig)Ser.Deserialize(fs);
            }
            catch { return null; }
        }

        public static void Delete(string name)
        {
            try
            {
                var path = PathFor(name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* ignorar falha de exclusão */ }
        }

        public static void SaveLast(RailConfig config) { try { Save(LastName, config); } catch { } }
        public static RailConfig LoadLast()            => Load(LastName);

        private static string PathFor(string name)
        {
            var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(Dir, safe + Ext);
        }
    }
}
