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
            result.InclinationDeg = Math.Atan2(riser, tread) * 180.0 / Math.PI;

            // ── 3. Patamar intermediário ─────────────────────────────────────
            // Posicionado no degrau mais próximo do centro.
            // k (0-indexed) = último degrau da marcha inferior.
            // Minimiza a distância do patamar ao centro do vão para CenterStair=true.
            result.HasIntermediateLanding = config.HasIntermediateLanding;
            if (result.HasIntermediateLanding)
            {
                int k = Math.Max(0, Math.Min(steps / 2 - 1, steps - 2));
                result.IntermediateLandingStep   = k + 1;             // 1-indexed
                result.IntermediateLandingLength = config.IntermediateLandingLength;
                result.IntermediateLandingAt     = (k + 1) * riser;   // altura em mm
            }

            // ── 4. Patamares de extremidade ──────────────────────────────────
            // EndLandings já desconta o ILL do espaço disponível.
            var (lower, upper) = LandingCalculator.EndLandings(result.TotalRun, beamDistance, config);
            result.LowerLandingDepth = lower;
            result.UpperLandingDepth = upper;

            const double minLanding = 250;
            if ((lower > 0 && lower < minLanding) || (upper > 0 && upper < minLanding))
            {
                result.Warnings.Add(
                    $"Atenção: patamar(es) abaixo de {minLanding:F0}mm " +
                    $"(inferior={lower:F0}mm, superior={upper:F0}mm). " +
                    "Recomendado afastar as vigas.");
            }

            var totalSpan = result.TotalRun + (result.HasIntermediateLanding ? config.IntermediateLandingLength : 0);
            if (totalSpan > beamDistance)
            {
                result.IsValid = false;
                result.Warnings.Add(
                    $"Desenvolvimento ({totalSpan:F0} mm) excede a distância entre vigas ({beamDistance:F0} mm). " +
                    "Reduza a pisada ou o número de degraus.");
            }

            return result;
        }
    }
}
