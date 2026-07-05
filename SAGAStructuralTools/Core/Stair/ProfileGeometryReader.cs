using Autodesk.Revit.DB;
using System.Linq;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Lê metadados geométricos do FamilySymbol para calcular o offset lateral
    /// que posiciona cada banzo com a face de referência correta em ±Width/2 do centro.
    ///
    ///   W/I (simétrico)  → offset = bf/2  (face interna do flange em Width/2)
    ///   U/Canal          → offset = bf/2  (face das costas/alma em Width/2)
    ///   U/Canal useAxis  → offset = 0     (eixo centroide-a-centroide)
    ///
    /// Ambos os perfis usam bf/2 porque as famílias Revit posicionam a seção com o
    /// origin no centro do bounding box (bf/2 da face extrema), não no centroide.
    /// Comprovado empiricamente: com e₀=16,2mm o model mede 775mm; com bf/2=28,7mm mede 800mm.
    /// </summary>
    public static class ProfileGeometryReader
    {
        private static readonly string[] ChannelKeywords =
            { "UPN", "UPE", "UAL", "CANAL", "DOBRAD", "CALHA", "_U_", "-U_", "_U-" };

        // Parâmetros de largura total da seção (bf para W/I, b para U/Canal)
        private static readonly string[] WidthParams =
            { "Gerdau_bf", "b", "bf", "B", "b1", "bf1", "Aba", "FlangeWidth", "Width", "Largura" };

        // ── API pública ───────────────────────────────────────────────────────

        /// <summary>
        /// Detecta se a família é um perfil U/Canal pelo nome.
        /// </summary>
        public static bool IsChannel(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return false;
            var up = familyName.ToUpperInvariant();

            if (ChannelKeywords.Any(k => up.Contains(k))) return true;

            if (up.Contains(" U ") || up.EndsWith(" U") || up.StartsWith("U ")) return true;

            if (up.StartsWith("U_") || up.StartsWith("U-")) return true;
            if (up.StartsWith("U") && up.Length > 1 && char.IsDigit(up[1])) return true;

            return false;
        }

        /// <summary>
        /// Calcula o offset lateral (mm) a somar a Width/2 para posicionar o eixo do banzo.
        /// Para W/I e U/Canal: retorna bf/2 (bounding-box centrado → face correta em Width/2).
        /// Para U/Canal com useAxis=true: retorna 0 (eixo-a-eixo).
        /// </summary>
        public static double GetFaceOffset(FamilySymbol symbol, bool isChannel, bool useAxis,
                                           out string source)
        {
            if (isChannel && useAxis)
            {
                source = "useAxis=true → eixo-a-eixo, offset=0";
                return 0;
            }

            // W/I e U/Canal (useAxis=false): offset = bf/2.
            // O origin da família está no centro do bounding box da seção.
            //   W/I: centroide = bounding-box center → face interna do flange em halfFt − bf/2.
            //   U/Canal costas-a-costas: bounding-box center em bf/2 da alma →
            //     face das costas em halfFt − bf/2. Mesmo resultado, mesma fórmula.
            double? bf = TryReadParam(symbol, WidthParams, out var pName);
            var kind   = isChannel ? "U" : "W/I";
            source = bf.HasValue
                ? $"{kind}: bf/2={bf.Value / 2:F1}mm (param '{pName}')"
                : $"{kind}: bf não lido, offset=0";
            return bf.HasValue ? bf.Value / 2.0 : 0;
        }

        // ── Helper ───────────────────────────────────────────────────────────

        private static double? TryReadParam(FamilySymbol symbol, string[] names, out string matched)
        {
            matched = null;
            foreach (var name in names)
            {
                var p = symbol.LookupParameter(name);
                if (p?.StorageType == StorageType.Double && p.AsDouble() > 1e-6)
                {
                    matched = name;
                    return p.AsDouble() * 304.8; // pés → mm
                }
            }
            return null;
        }
    }
}
