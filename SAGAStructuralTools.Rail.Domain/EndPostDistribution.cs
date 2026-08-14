using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Rail.Domain
{
    public sealed class EndPostDistribution
    {
        public IReadOnlyList<double> StationsMm { get; }
        public double ActualSpacingMm { get; }

        private EndPostDistribution(IReadOnlyList<double> stationsMm, double actualSpacingMm)
        {
            StationsMm = stationsMm;
            ActualSpacingMm = actualSpacingMm;
        }

        public static EndPostDistribution Create(double fixedEndStationMm, double movedEndStationMm, int postCount)
        {
            if (postCount < 2)
                throw new ArgumentOutOfRangeException(nameof(postCount), "A distribuição exige ao menos dois montantes.");

            double length = Math.Abs(movedEndStationMm - fixedEndStationMm);
            if (length < 1.0)
                throw new ArgumentException("As extremidades precisam estar afastadas pelo menos 1 mm.");

            double start = Math.Min(fixedEndStationMm, movedEndStationMm);
            double end = Math.Max(fixedEndStationMm, movedEndStationMm);
            double spacing = (end - start) / (postCount - 1);
            var stations = new List<double>(postCount);
            for (int index = 0; index < postCount; index++)
                stations.Add(Math.Round(start + spacing * index, 4));

            return new EndPostDistribution(stations, spacing);
        }

        public static int RequiredPostCount(double lengthMm, double maximumSpacingMm)
        {
            if (lengthMm < 1.0) throw new ArgumentOutOfRangeException(nameof(lengthMm));
            if (maximumSpacingMm < 1.0) throw new ArgumentOutOfRangeException(nameof(maximumSpacingMm));
            return Math.Max(2, (int)Math.Ceiling(lengthMm / maximumSpacingMm) + 1);
        }
    }
}
