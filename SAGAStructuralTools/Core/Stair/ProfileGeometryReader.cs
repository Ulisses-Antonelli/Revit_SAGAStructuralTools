using Autodesk.Revit.DB;
using System.Linq;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Lê metadados geométricos do FamilySymbol para calcular o offset lateral
    /// que posiciona cada banzo respeitando a face de referência correta.
    ///
    ///   Perfil W/I (simétrico)
    ///     → offset = bf/2  (extremidade interna da mesa)
    ///     → a escada fica entre as duas mesas internas
    ///
    ///   Perfil U/Canal (assimétrico, costas-a-costas)
    ///     → useAxis=false: offset = e₀  (face externa da alma = "costas")
    ///     → useAxis=true : offset = 0   (eixo neutro centroide-a-centroide)
    /// </summary>
    public static class ProfileGeometryReader
    {
        private static readonly string[] ChannelKeywords =
            { "UPN", "UPE", "UAL", "CANAL", "DOBRAD", "CALHA", "_U_", "-U_", "_U-" };

        // Parâmetros comuns para largura da seção (bf para W, b para U)
        private static readonly string[] WidthParams =
            { "b", "bf", "B", "b1", "bf1", "Aba", "Largura", "FlangeWidth", "Width" };

        // Parâmetros comuns para offset centroide→alma em perfis U (e₀)
        private static readonly string[] WebOffsetParams =
            { "e0", "xg", "Xg", "xG", "ecg", "cx", "e_s", "CentroideX" };

        // ── API pública ───────────────────────────────────────────────────────

        /// <summary>
        /// Detecta se a família é um perfil U/Canal (assimétrico) pelo nome.
        /// </summary>
        public static bool IsChannel(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return false;
            var up = familyName.ToUpperInvariant();

            if (ChannelKeywords.Any(k => up.Contains(k))) return true;

            // "U" como palavra isolada separada por espaço (ex: "Viga U Gerdau")
            if (up.Contains(" U ") || up.EndsWith(" U") || up.StartsWith("U ")) return true;

            // "U_", "U-" ou "U" + dígito no início (ex: "U_PerfilDobrado", "U100")
            if (up.StartsWith("U_") || up.StartsWith("U-")) return true;
            if (up.StartsWith("U") && up.Length > 1 && char.IsDigit(up[1])) return true;

            return false;
        }

        /// <summary>
        /// Calcula o offset lateral (mm) do centroide do banzo até a face de referência.
        /// Esse valor é somado a Width/2 para obter a posição do eixo de cada banzo.
        ///
        /// <paramref name="source"/> descreve como o valor foi obtido (para log).
        /// </summary>
        public static double GetFaceOffset(FamilySymbol symbol, bool isChannel, bool useAxis,
                                           out string source)
        {
            if (isChannel && useAxis)
            {
                source = "useAxis=true → eixo-a-eixo, offset=0";
                return 0;
            }

            if (!isChannel)
            {
                // W/I: soma bf/2 → extremidade interna da mesa fica em Width/2
                double? bf = TryReadWidth(symbol, out var pName);
                source = bf.HasValue
                    ? $"W/I: bf/2={bf.Value / 2:F1}mm (param '{pName}')"
                    : "W/I: bf não lido, offset=0";
                return bf.HasValue ? bf.Value / 2.0 : 0;
            }
            else
            {
                // U/Canal: soma e₀ → face externa da alma fica em Width/2
                double? e0 = TryReadWebOffset(symbol, out var e0Name);
                if (e0.HasValue)
                {
                    source = $"U: e₀={e0.Value:F1}mm (param '{e0Name}')";
                    return e0.Value;
                }

                // e₀ não encontrado como parâmetro; estima via largura total
                double? b = TryReadWidth(symbol, out var bName);
                if (b.HasValue)
                {
                    double approx = b.Value * 0.29; // média de canais padrão: e₀ ≈ 28-31% de b
                    source = $"U: e₀≈{approx:F1}mm (29% de b={b.Value:F0}mm, param '{bName}')";
                    return approx;
                }

                source = "U: parâmetros não lidos, offset=0 (medida pelo eixo)";
                return 0;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static double? TryReadWidth(FamilySymbol symbol, out string paramName)
        {
            paramName = null;
            foreach (var name in WidthParams)
            {
                var p = symbol.LookupParameter(name);
                if (p?.StorageType == StorageType.Double && p.AsDouble() > 1e-6)
                {
                    paramName = name;
                    return p.AsDouble() * 304.8; // pés → mm
                }
            }
            return null;
        }

        private static double? TryReadWebOffset(FamilySymbol symbol, out string paramName)
        {
            paramName = null;
            foreach (var name in WebOffsetParams)
            {
                var p = symbol.LookupParameter(name);
                if (p?.StorageType == StorageType.Double && p.AsDouble() > 1e-6)
                {
                    paramName = name;
                    return p.AsDouble() * 304.8;
                }
            }
            return null;
        }
    }
}
