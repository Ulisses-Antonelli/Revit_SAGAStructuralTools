using System;
using SAGAStructuralTools.Core.Models;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Motor de cálculo geométrico da escada. Zero dependência de Revit.
    /// Entrada: desnível + distância entre vigas + configuração.
    /// Saída: StairDefinition completo para pré-visualização e criação.
    /// </summary>
    public static class StairCalculator
    {
        public static StairDefinition Calculate(
            double totalRise,    // mm — desnível entre vigas
            double beamDistance, // mm — distância horizontal entre vigas
            StairConfig config)
        {
            var result = new StairDefinition
            {
                TotalRise    = totalRise,
                BeamDistance = beamDistance,
                TreadDepth   = config.TreadDepth
            };

            if (totalRise <= 0 || beamDistance <= 0)
            {
                result.IsValid = false;
                result.Warnings.Add("Desnível ou distância entre vigas inválidos.");
                return result;
            }

            // ── 1. Número de degraus e espelho ──────────────────────────────
            int    steps;
            double riser;
            double tread;

            if (config.ApplyBlondel)
            {
                (steps, riser, tread) = BlondelRule.BestFit(totalRise, config.TreadDepth);
                result.Warnings.Add($"Blondel aplicado: pisada ajustada para {tread:F1} mm.");
            }
            else
            {
                steps = (int)Math.Round(totalRise / StairDefaults.TargetRiserHeight);
                if (steps < 2) steps = 2;
                riser = totalRise / steps;
                tread = config.TreadDepth;
            }

            result.StepCount     = steps;
            result.RiserHeight   = riser;
            result.TreadDepth    = tread;
            result.TotalRun      = steps * tread;

            // ── 2. Inclinação ────────────────────────────────────────────────
            // Calculada pelo vértice superior da pisada (conforme spec)
            result.InclinationDeg = Math.Atan2(riser, tread) * 180.0 / Math.PI;

            // ── 3. Patamares de extremidade ──────────────────────────────────
            var (lower, upper) = LandingCalculator.EndLandings(result.TotalRun, beamDistance, config);
            result.LowerLandingDepth = lower;
            result.UpperLandingDepth = upper;

            if (result.TotalRun > beamDistance)
            {
                result.IsValid = false;
                result.Warnings.Add(
                    $"Desenvolvimento ({result.TotalRun:F0} mm) excede a distância entre vigas ({beamDistance:F0} mm). " +
                    "Reduza a pisada ou o número de degraus.");
            }

            // ── 4. Patamar intermediário ─────────────────────────────────────
            result.HasIntermediateLanding = LandingCalculator.NeedsIntermediate(totalRise, config);
            if (result.HasIntermediateLanding)
            {
                result.IntermediateLandingAt = LandingCalculator.IntermediatePosition(steps, riser);
                if (config.IntermediateLanding == LandingMode.Never)
                    result.Warnings.Add($"Altura ({totalRise:F0} mm) excede o limite normativo " +
                        $"({config.MaxHeightWithoutLanding:F0} mm) — patamar intermediário suprimido.");
            }

            return result;
        }
    }
}
