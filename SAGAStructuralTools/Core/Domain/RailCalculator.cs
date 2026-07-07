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

            switch (config.DistMode)
            {
                case DistributionMode.ByCount:
                    ByCount(seg, config.PostCount);
                    break;

                case DistributionMode.MaxSpan:
                    int n = (int)Math.Ceiling(seg.Length / config.MaxPostSpan) + 1;
                    ByCount(seg, n);
                    break;

                case DistributionMode.FixedAxis:
                    FixedAxis(seg, config.FixedAxisSpacing);
                    break;
            }
        }

        private static void ByCount(RailSegment seg, int count)
        {
            count = Math.Max(count, 2);
            double step = seg.Length / (count - 1);
            for (int i = 0; i < count; i++)
                seg.PostOffsets.Add(Math.Round(i * step, 4));
            seg.ActualSpacing = step;
        }

        private static void FixedAxis(RailSegment seg, double step)
        {
            step = Math.Max(step, 1.0);
            double pos = 0;
            while (pos < seg.Length - 0.5)
            {
                seg.PostOffsets.Add(Math.Round(pos, 4));
                pos += step;
            }
            // Post final no fim exato do segmento
            if (seg.PostOffsets.Count == 0 || seg.Length - seg.PostOffsets[seg.PostOffsets.Count - 1] > 0.5)
                seg.PostOffsets.Add(seg.Length);
            seg.ActualSpacing = step;
        }
    }
}
