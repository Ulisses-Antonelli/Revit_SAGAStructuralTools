using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SAGAStructuralTools.Core.Mapping
{
    public class GerdauCatalog
    {
        // índice principal: nome normalizado → (caminho rfa, nome exato no catálogo)
        private readonly Dictionary<string, (string FamilyPath, string TypeName)> _beamIndex
            = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, (string FamilyPath, string TypeName)> _columnIndex
            = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        // índice secundário para perfis U imperiais: "U {depth} {mass:F1}" → tipo
        private readonly Dictionary<string, (string FamilyPath, string TypeName)> _uBeamMassIndex
            = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, (string FamilyPath, string TypeName)> _uColumnMassIndex
            = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        // detecta famílias U pelo nome do arquivo
        private static readonly Regex UFamilyNamePattern =
            new Regex(@"\bU\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // extrai profundidade de tipo U: "U 8""x1ª Alma" → "8"
        private static readonly Regex UDepthPattern =
            new Regex("U\\s*(\\d+)\"", RegexOptions.Compiled);

        public int Count        => _beamIndex.Count + _columnIndex.Count;
        public int BeamCount    => _beamIndex.Count;
        public int ColumnCount  => _columnIndex.Count;

        public void Load(string directory)
        {
            _beamIndex.Clear();
            _columnIndex.Clear();
            _uBeamMassIndex.Clear();
            _uColumnMassIndex.Clear();

            if (!Directory.Exists(directory)) return;

            foreach (var rfaPath in Directory.GetFiles(directory, "*.rfa", SearchOption.AllDirectories))
            {
                var catalogPath = Path.ChangeExtension(rfaPath, ".txt");
                var isColumn    = IsColumnFamily(rfaPath);
                var isUFamily   = UFamilyNamePattern.IsMatch(Path.GetFileNameWithoutExtension(rfaPath));

                if (File.Exists(catalogPath))
                    IndexTypeCatalog(rfaPath, catalogPath, isColumn, isUFamily);
                else
                    IndexSingleFamily(rfaPath, isColumn);
            }
        }

        private static bool IsColumnFamily(string rfaPath)
        {
            var name = Path.GetFileNameWithoutExtension(rfaPath);
            // "Viga" tem prioridade — se o arquivo se declara viga, nunca é coluna
            if (name.IndexOf("Viga", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            // Gerdau usa "Pilar"; ArcelorMittal e outros usam "Coluna"
            return name.IndexOf("Pilar",  StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Coluna", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void IndexSingleFamily(string rfaPath, bool isColumn)
        {
            var name  = Path.GetFileNameWithoutExtension(rfaPath);
            var key   = ProfileMatcher.Normalize(name.Replace("\"\"", "\""));
            var index = isColumn ? _columnIndex : _beamIndex;
            if (!index.ContainsKey(key))
                index[key] = (rfaPath, name);
        }

        private void IndexTypeCatalog(string rfaPath, string catalogPath, bool isColumn, bool isUFamily)
        {
            var mainIndex    = isColumn ? _columnIndex : _beamIndex;
            var uMassIndex   = isColumn ? _uColumnMassIndex : _uBeamMassIndex;

            try
            {
                var lines = File.ReadAllLines(catalogPath, Encoding.Default);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.TrimStart().StartsWith("##")) continue;

                    var fields   = line.Split(new[] { ',' }, StringSplitOptions.None);
                    var typeName = fields[0].Trim();

                    // Remove aspas envolventes de campos CSV (mas preserva "" internos = símbolo de polegada)
                    if (typeName.StartsWith("\"") && typeName.EndsWith("\"") && typeName.Length >= 2)
                        typeName = typeName.Substring(1, typeName.Length - 2);

                    if (string.IsNullOrWhiteSpace(typeName)) continue;
                    if (typeName.Contains("##")) continue;

                    // Converte "" → " (símbolo de polegada) — é o nome real do tipo no Revit
                    var cleanName = typeName.Replace("\"\"", "\"");
                    var key       = ProfileMatcher.Normalize(cleanName);

                    // Armazena cleanName (sem "") porque é o que LoadFamilySymbol espera
                    if (!mainIndex.ContainsKey(key))
                        mainIndex[key] = (rfaPath, cleanName);

                    // Para famílias U: índice secundário por massa linear (campo 11)
                    if (isUFamily && fields.Length > 11)
                    {
                        if (double.TryParse(fields[11].Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double massKgm))
                        {
                            var depthMatch = UDepthPattern.Match(cleanName);
                            if (depthMatch.Success)
                            {
                                var uKey = $"U {depthMatch.Groups[1].Value} {massKgm:F1}";
                                if (!uMassIndex.ContainsKey(uKey))
                                    uMassIndex[uKey] = (rfaPath, cleanName);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public (string FamilyPath, string TypeName)? FindBeam(string normalizedName)
        {
            if (_beamIndex.TryGetValue(normalizedName, out var e)) return e;
            return null;
        }

        public (string FamilyPath, string TypeName)? FindColumn(string normalizedName)
        {
            if (_columnIndex.TryGetValue(normalizedName, out var e)) return e;
            return null;
        }

        /// <summary>Busca perfil U imperial pelo depth (em polegadas) e massa linear (kg/m).</summary>
        public (string FamilyPath, string TypeName)? FindBeamByUMass(string depthInches, double massKgm)
        {
            var key = $"U {depthInches} {massKgm:F1}";
            if (_uBeamMassIndex.TryGetValue(key, out var e)) return e;
            return null;
        }

        public (string FamilyPath, string TypeName)? FindColumnByUMass(string depthInches, double massKgm)
        {
            var key = $"U {depthInches} {massKgm:F1}";
            if (_uColumnMassIndex.TryGetValue(key, out var e)) return e;
            return null;
        }
    }
}
