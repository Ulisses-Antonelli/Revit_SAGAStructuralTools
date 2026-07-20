using SAGAStructuralTools.Core.Models;
using System;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Calcula posições de degraus, suportes e anéis da escada marinheiro e emite
    /// os avisos normativos. Cotas de saída medidas a partir da BASE (mm).
    /// Sem dependência da Revit API — totalmente testável de forma isolada.
    /// </summary>
    public static class LadderCalculator
    {
        private const double MinHeightMm = 500.0;

        public static LadderDefinition Calculate(double totalHeightMm, LadderConfig config)
        {
            var def = new LadderDefinition { TotalHeightMm = totalHeightMm };

            if (config == null)
            {
                def.IsValid = false;
                def.Warnings.Add("Configuração indisponível.");
                return def;
            }

            if (totalHeightMm < MinHeightMm)
            {
                def.IsValid = false;
                def.Warnings.Add(
                    $"Altura total muito pequena ({totalHeightMm:F0} mm). Verifique a referência superior e o nível-base.");
                return def;
            }

            ComputeRungs(def, totalHeightMm, config);
            ComputeSupports(def, totalHeightMm, config);
            ComputeCage(def, totalHeightMm, config);
            def.ExtensionTopMm = totalHeightMm + Math.Max(config.ExtensionHeight, 0);

            AddNormativeWarnings(def, totalHeightMm, config);
            return def;
        }

        // Degraus: o superior nivela com o desembarque e os demais descem em passo
        // uniforme. A folga restante fica na base (prática usual de fabricação).
        private static void ComputeRungs(LadderDefinition def, double heightMm, LadderConfig config)
        {
            double spacing = Math.Max(config.RungSpacing, 50.0);
            const double minBottomClearance = 100.0;

            for (double z = heightMm; z >= minBottomClearance; z -= spacing)
                def.RungElevations.Add(Math.Round(z, 1));

            def.RungElevations.Reverse();   // base → topo
            def.BottomGapMm = def.RungElevations.Count > 0 ? def.RungElevations[0] : heightMm;

            if (def.RungElevations.Count < 2)
            {
                def.IsValid = false;
                def.Warnings.Add("Menos de 2 degraus — verifique a altura e o espaçamento.");
            }
        }

        // Suportes: distribuídos uniformemente respeitando o vão máximo informado.
        private static void ComputeSupports(LadderDefinition def, double heightMm, LadderConfig config)
        {
            const double edgeMargin = 300.0;   // afastamento das extremidades
            double usable = heightMm - 2 * edgeMargin;
            if (usable <= 0)
            {
                def.SupportElevations.Add(Math.Round(heightMm / 2.0, 1));
                return;
            }

            double maxSpan = Math.Max(config.SupportMaxSpacing, 300.0);
            int spans = Math.Max(1, (int)Math.Ceiling(usable / maxSpan));
            double step = usable / spans;

            for (int i = 0; i <= spans; i++)
                def.SupportElevations.Add(Math.Round(edgeMargin + i * step, 1));
        }

        // Anéis da gaiola: do início até o desembarque. Equidistante recalcula o passo
        // para fechar exatamente no topo; passo fixo acrescenta um anel final no topo
        // quando a sobra for relevante.
        private static void ComputeCage(LadderDefinition def, double heightMm, LadderConfig config)
        {
            if (!config.HasCage) return;

            double startZ = Math.Max(config.CageStartHeight, 0);
            if (startZ >= heightMm - 100.0)
            {
                def.Warnings.Add(
                    "A altura de início da gaiola está acima (ou muito próxima) do desembarque — nenhum anel será criado.");
                return;
            }

            double spacing = Math.Max(config.RingSpacing, 100.0);
            double span = heightMm - startZ;

            if (config.RingMode == RingDistribution.Equidistant)
            {
                int spans = Math.Max(1, (int)Math.Ceiling(span / spacing));
                double step = span / spans;
                for (int i = 0; i <= spans; i++)
                    def.RingElevations.Add(Math.Round(startZ + i * step, 1));
            }
            else
            {
                for (double z = startZ; z <= heightMm + 0.5; z += spacing)
                    def.RingElevations.Add(Math.Round(z, 1));
                double last = def.RingElevations[def.RingElevations.Count - 1];
                if (heightMm - last > 50.0)
                    def.RingElevations.Add(Math.Round(heightMm, 1));
            }

            def.StrapCount = Math.Max(config.StrapCount, 0);
        }

        private static void AddNormativeWarnings(LadderDefinition def, double heightMm, LadderConfig config)
        {
            if (config.Width < LadderDefaults.MinWidth)
                def.Warnings.Add(
                    $"Largura útil de {config.Width:F0} mm abaixo do mínimo normativo ({LadderDefaults.MinWidth:F0} mm).");
            else if (config.Width > LadderDefaults.MaxWidth)
                def.Warnings.Add(
                    $"Largura útil de {config.Width:F0} mm acima do usual normativo ({LadderDefaults.MaxWidth:F0} mm).");

            if (config.RungSpacing > LadderDefaults.MaxRungSpacing)
                def.Warnings.Add(
                    $"Espaçamento entre degraus de {config.RungSpacing:F0} mm acima do máximo normativo ({LadderDefaults.MaxRungSpacing:F0} mm).");

            if (config.WallOffset < LadderDefaults.MinWallOffset)
                def.Warnings.Add(
                    $"Afastamento da estrutura de {config.WallOffset:F0} mm abaixo do mínimo normativo ({LadderDefaults.MinWallOffset:F0} mm).");

            if (!config.HasCage && heightMm > LadderDefaults.CageRequiredAbove)
                def.Warnings.Add(
                    $"Altura de {heightMm:F0} mm acima de {LadderDefaults.CageRequiredAbove:F0} mm — a norma recomenda gaiola de proteção (ou linha de vida).");

            if (config.HasCage && config.RingSpacing > LadderDefaults.MaxRingSpacing &&
                config.RingMode == RingDistribution.FixedSpacing)
                def.Warnings.Add(
                    $"Espaçamento entre anéis de {config.RingSpacing:F0} mm acima do máximo usual ({LadderDefaults.MaxRingSpacing:F0} mm).");

            if (config.ExtensionHeight < LadderDefaults.MinExtension)
                def.Warnings.Add(
                    $"Prolongamento de {config.ExtensionHeight:F0} mm abaixo do mínimo normativo ({LadderDefaults.MinExtension:F0} mm) para pegada no desembarque.");
        }
    }
}
