using SAGAStructuralTools.Core.Mapping;
using SAGAStructuralTools.Core.Models;
using System;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Casa a designação de perfil do Robot (ex.: "W 460x52", "U 250x100x3.75", "HP 200x53") com o
    /// catálogo Gerdau. Diferente do ProfileMatcher (usado no conversor de IFC), aqui não precisa de
    /// regex de EXTRAÇÃO — o parser do Robot já entrega a designação isolada e limpa, sem precisar
    /// achá-la embutida num nome IFC ruidoso. Só precisa da mesma normalização de espaçamento que o
    /// ProfileMatcher já faz (W 460x52 → W 460 x 52; U 250x100x3.75 → U 250 x 100 x 3.75 — mesma
    /// forma que o catálogo Gerdau já usa pros perfis U dobrados vindos do STRAP).
    ///
    /// Não validado ainda contra o catálogo .rfa real do usuário — se o nome de Tipo no catálogo usar
    /// capitalização/espaçamento diferente (ex.: "W 460 X 52" com X maiúsculo), o casamento falha e
    /// aparece como aviso "não encontrado", não como erro silencioso.
    /// </summary>
    public static class RobotProfileResolver
    {
        private const double ThicknessToleranceMm = 0.1;

        public static ProfileMapping Resolve(GerdauCatalog catalog, RobotProfileAssignment assignment, bool isColumn)
        {
            if (assignment == null || string.IsNullOrWhiteSpace(assignment.RawDesignation)) return null;

            var normalized = ProfileMatcher.Normalize(assignment.RawDesignation);
            var entry = isColumn ? catalog.FindColumn(normalized) : catalog.FindBeam(normalized);
            if (entry == null) return null;

            return Build(assignment.RawDesignation, entry.Value);
        }

        /// <summary>
        /// Como Resolve, mas se a busca exata falhar e o perfil for um U dobrado, tenta achar na
        /// MESMA família (viga ou pilar, o mesmo papel) uma entrada com altura/largura idênticas e
        /// espessura dentro de uma tolerância pequena — cobre o caso real observado de o Robot
        /// exportar a espessura arredondada/truncada (ex.: "6.3" quando o catálogo tem "6.35", que é
        /// o mesmo perfil físico, não um perfil diferente).
        /// </summary>
        public static ProfileMapping ResolveWithTolerance(GerdauCatalog catalog, RobotProfileAssignment assignment, bool isColumn)
        {
            var exact = Resolve(catalog, assignment, isColumn);
            if (exact != null) return exact;

            var target = UDobradoProfileFormat.TryParse(assignment?.RawDesignation);
            if (target == null) return null;

            var entries = isColumn ? catalog.ColumnEntries : catalog.BeamEntries;
            (string FamilyPath, string TypeName)? best = null;
            double bestDiff = double.MaxValue;

            foreach (var kv in entries)
            {
                var candidate = UDobradoProfileFormat.TryParse(kv.Key);
                if (candidate == null) continue;
                if (Math.Abs(candidate.HeightMm - target.HeightMm) > 1e-6) continue;
                if (Math.Abs(candidate.WidthMm - target.WidthMm) > 1e-6) continue;

                double diff = Math.Abs(candidate.ThicknessMm - target.ThicknessMm);
                if (diff <= ThicknessToleranceMm && diff < bestDiff)
                {
                    bestDiff = diff;
                    best = kv.Value;
                }
            }

            return best.HasValue ? Build(assignment.RawDesignation, best.Value) : null;
        }

        private static ProfileMapping Build(string rawDesignation, (string FamilyPath, string TypeName) entry)
            => new ProfileMapping
            {
                IfcName = rawDesignation,
                GerdauName = System.IO.Path.GetFileNameWithoutExtension(entry.FamilyPath),
                FamilyPath = entry.FamilyPath,
                FamilyType = entry.TypeName
            };
    }
}
