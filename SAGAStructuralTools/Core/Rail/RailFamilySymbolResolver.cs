using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SAGAStructuralTools.Core.Rail
{
    /// <summary>
    /// Resolve tipos de família tolerando diferenças invisíveis introduzidas por
    /// catálogos, presets e projetos produzidos em outros computadores.
    /// </summary>
    internal static class RailFamilySymbolResolver
    {
        internal static FamilySymbol Resolve(Document document, string path, string typeName)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("O caminho da família não foi informado.");

            string familyName = Clean(Path.GetFileNameWithoutExtension(path));
            string requestedType = Clean(typeName);
            if (string.IsNullOrWhiteSpace(requestedType))
                throw new InvalidOperationException(
                    $"O tipo da família '{familyName}' não foi informado.");

            var symbols = CollectSymbols(document);
            var existing = FindEquivalent(symbols, familyName, requestedType);
            if (existing != null) return existing;

            FamilySymbol loaded;
            bool loadSucceeded = document.LoadFamilySymbol(path, requestedType, out loaded);
            if (loadSucceeded && loaded != null) return loaded;

            // Algumas versões do Revit retornam false quando a família homônima já
            // existe, mesmo que a operação tenha disponibilizado o tipo solicitado.
            symbols = CollectSymbols(document);
            existing = FindEquivalent(symbols, familyName, requestedType);
            if (existing != null) return existing;

            var availableTypes = symbols
                .Where(s => Equivalent(s.Family?.Name, familyName))
                .Select(s => Clean(s.Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .Take(12)
                .ToList();

            string details = availableTypes.Count == 0
                ? "Nenhum tipo dessa família foi encontrado no documento."
                : "Tipos encontrados no documento: " + string.Join("; ", availableTypes);

            throw new InvalidOperationException(
                $"Não foi possível resolver '{familyName}' tipo '{requestedType}'. {details}");
        }

        private static List<FamilySymbol> CollectSymbols(Document document) =>
            new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

        private static FamilySymbol FindEquivalent(
            IEnumerable<FamilySymbol> symbols,
            string familyName,
            string typeName) =>
            symbols.FirstOrDefault(symbol =>
                Equivalent(symbol.Family?.Name, familyName) &&
                Equivalent(symbol.Name, typeName));

        private static bool Equivalent(string left, string right)
        {
            string cleanLeft = Clean(left);
            string cleanRight = Clean(right);
            if (string.Equals(cleanLeft, cleanRight, StringComparison.OrdinalIgnoreCase))
                return true;

            string leftSignature = Signature(cleanLeft);
            string rightSignature = Signature(cleanRight);
            return leftSignature.Length > 0 &&
                   string.Equals(leftSignature, rightSignature, StringComparison.Ordinal);
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var result = new StringBuilder(value.Length);
            bool pendingSpace = false;
            foreach (char character in value.Normalize(NormalizationForm.FormKC))
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (character == '\0' ||
                    category == UnicodeCategory.Control ||
                    category == UnicodeCategory.Format)
                    continue;

                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    result.Append(' ');
                    pendingSpace = false;
                }
                result.Append(character);
            }
            return result.ToString().Trim();
        }

        private static string Signature(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return new string(value
                .Where(character => character <= 127 &&
                    (char.IsLetterOrDigit(character) ||
                     character == '.' || character == ',' ||
                     character == '/' || character == '"'))
                .Select(char.ToUpperInvariant)
                .ToArray());
        }
    }
}
