using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Domain
{
    public static class RailCalculator
    {
        /// <summary>
        /// Calcula as posições dos montantes para cada segmento.
        /// Sem dependência da Revit API — totalmente testável de forma isolada.
        /// </summary>
        public static RailDefinition Calculate(List<double> segmentLengthsMm, RailConfig config)
        {
            var def = new RailDefinition();

            if (segmentLengthsMm == null || segmentLengthsMm.Count == 0)
            {
                def.IsValid = false;
                def.Warnings.Add("Nenhum segmento selecionado.");
                return def;
            }

            for (int i = 0; i < segmentLengthsMm.Count; i++)
            {
                var seg = new RailSegment { Index = i, Length = segmentLengthsMm[i] };
                ComputePosts(seg, config, def.Warnings);
                def.Segments.Add(seg);
            }

            def.IsValid = def.Warnings.Count == 0;
            return def;
        }

        private static void ComputePosts(RailSegment seg, RailConfig config, List<string> warnings)
        {
            if (seg.Length < 1.0)
            {
                warnings.Add($"Segmento {seg.Index + 1}: comprimento muito pequeno ({seg.Length:F0} mm).");
                return;
            }

            double inset = config.EndPostInset;
            if (inset < 0)
            {
                warnings.Add($"Segmento {seg.Index + 1}: o recuo das extremidades não pode ser negativo.");
                return;
            }

            double start = inset;
            double end = seg.Length - inset;
            double usableLength = end - start;
            if (usableLength < 1.0)
            {
                warnings.Add(
                    $"Segmento {seg.Index + 1}: o recuo de {inset:F0} mm em cada extremidade " +
                    $"não cabe no comprimento de {seg.Length:F0} mm.");
                return;
            }

            switch (config.DistMode)
            {
                case DistributionMode.ByCount:
                    ByCount(seg, config.PostCount, start, end);
                    break;

                case DistributionMode.MaxSpan:
                    double maxSpan = Math.Max(config.MaxPostSpan, 1.0);
                    int n = (int)Math.Ceiling(usableLength / maxSpan) + 1;
                    ByCount(seg, n, start, end);
                    break;

                case DistributionMode.FixedAxis:
                    FixedAxis(seg, config.FixedAxisSpacing, start, end);
                    break;
            }
        }

        private static void ByCount(RailSegment seg, int count, double start, double end)
        {
            count = Math.Max(count, 2);
            double step = (end - start) / (count - 1);
            for (int i = 0; i < count; i++)
                seg.PostOffsets.Add(Math.Round(start + i * step, 4));
            seg.ActualSpacing = step;
        }

        private static void FixedAxis(RailSegment seg, double step, double start, double end)
        {
            step = Math.Max(step, 1.0);
            double pos = start;
            while (pos < end - 0.5)
            {
                seg.PostOffsets.Add(Math.Round(pos, 4));
                pos += step;
            }
            // Último montante no recuo exato da extremidade final.
            if (seg.PostOffsets.Count == 0 || end - seg.PostOffsets[seg.PostOffsets.Count - 1] > 0.5)
                seg.PostOffsets.Add(Math.Round(end, 4));
            seg.ActualSpacing = step;
        }
    }
}
