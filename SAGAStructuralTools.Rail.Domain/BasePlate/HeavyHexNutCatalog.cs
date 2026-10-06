using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SAGAStructuralTools.BasePlate.Domain
{
    public static class HeavyHexNutCatalog
    {
        public const string Standard = "ANSI B 18.2.2";
        public const string Type = "Porca sextavada pesada";
        public const string ThreadClass = "2B";

        public static IReadOnlyList<HeavyHexNutOption> Options { get; } =
            new ReadOnlyCollection<HeavyHexNutOption>(new[]
            {
                new HeavyHexNutOption("1/4\" (6,35 mm)", 6.35, 12.39, 12.70, 5.54, 6.35, 14.1, 14.6, 11.8, 7.1, 0.53),
                new HeavyHexNutOption("5/16\" (7,94 mm)", 7.94, 13.87, 14.27, 7.11, 7.97, 15.8, 16.5, 13.2, 8.7, 0.76),
                new HeavyHexNutOption("3/8\" (9,53 mm)", 9.53, 17.00, 17.47, 8.68, 9.57, 19.4, 20.2, 16.2, 10.3, 1.42),
                new HeavyHexNutOption("7/16\" (11,11 mm)", 11.11, 18.49, 19.05, 10.23, 11.20, 21.1, 22.0, 17.6, 12.0, 1.88),
                new HeavyHexNutOption("1/2\" (12,70 mm)", 12.70, 21.59, 22.22, 11.78, 12.80, 24.6, 25.6, 20.5, 13.7, 2.98),
                new HeavyHexNutOption("9/16\" (14,29 mm)", 14.29, 23.09, 23.82, 13.36, 14.43, 26.3, 27.5, 21.9, 15.4, 3.69),
                new HeavyHexNutOption("5/8\" (15,88 mm)", 15.88, 26.18, 26.97, 14.91, 16.02, 29.9, 31.2, 24.9, 17.1, 5.39),
                new HeavyHexNutOption("3/4\" (19,05 mm)", 19.05, 30.78, 31.75, 18.03, 19.25, 35.1, 36.6, 29.3, 20.6, 8.74),
                new HeavyHexNutOption("7/8\" (22,23 mm)", 22.23, 35.40, 36.52, 21.16, 22.48, 40.4, 42.2, 33.6, 24.0, 13.45),
                new HeavyHexNutOption("1\" (25,40 mm)", 25.40, 40.00, 41.27, 24.26, 25.70, 45.6, 47.6, 38.0, 27.4, 19.25),
                new HeavyHexNutOption("1.1/8\" (28,58 mm)", 28.58, 44.50, 46.02, 27.40, 28.93, 50.9, 53.2, 42.4, 30.9, 26.82),
                new HeavyHexNutOption("1.1/4\" (31,75 mm)", 31.75, 49.22, 50.80, 30.15, 31.77, 56.1, 58.6, 46.8, 34.3, 35.60),
                new HeavyHexNutOption("1.3/8\" (34,93 mm)", 34.93, 53.82, 55.57, 33.27, 35.00, 61.4, 64.2, 51.1, 37.7, 46.21),
                new HeavyHexNutOption("1.1/2\" (38,10 mm)", 38.10, 58.42, 60.32, 36.40, 36.22, 66.6, 69.6, 55.5, 41.1, 59.34),
                new HeavyHexNutOption("1.5/8\" (41,28 mm)", 41.28, 63.01, 65.07, 39.52, 41.45, 71.8, 75.1, 59.9, 44.6, 73.39),
                new HeavyHexNutOption("1.3/4\" (44,45 mm)", 44.45, 67.61, 69.85, 42.65, 44.67, 77.1, 80.6, 64.2, 48.0, 92.41),
                new HeavyHexNutOption("1.7/8\" (47,63 mm)", 47.63, 72.24, 74.62, 45.77, 47.90, 82.4, 86.1, 68.6, 51.4, 109.2),
                new HeavyHexNutOption("2\" (50,80 mm)", 50.80, 76.83, 79.37, 48.89, 51.13, 87.6, 91.6, 73.0, 54.9, 135.5),
                new HeavyHexNutOption("2.1/4\" (57,15 mm)", 57.15, 86.06, 88.90, 54.74, 57.17, 98.1, 102.6, 81.8, 61.7, 189.8),
                new HeavyHexNutOption("2.1/2\" (63,50 mm)", 63.50, 95.25, 98.42, 60.98, 63.62, 108.6, 113.6, 90.5, 68.6, 255.5),
                new HeavyHexNutOption("2.3/4\" (69,85 mm)", 69.85, 104.44, 108.95, 67.23, 70.08, 119.1, 124.8, 99.2, 75.4, 334.3),
                new HeavyHexNutOption("3\" (76,20 mm)", 76.20, 113.86, 117.47, 73.48, 76.53, 129.8, 135.8, 108.0, 82.3, 430.4),
                new HeavyHexNutOption("3.1/4\" (82,55 mm)", 82.55, 122.88, 127.00, 79.35, 82.80, 140.1, 146.8, 116.7, 89.1, 541),
                new HeavyHexNutOption("3.1/2\" (88,90 mm)", 88.90, 132.88, 136.52, 85.80, 89.05, 150.6, 157.7, 125.5, 96.0, 691),
                new HeavyHexNutOption("3.3/4\" (95,25 mm)", 95.25, 141.27, 146.05, 91.85, 95.50, 161.1, 168.8, 134.2, 109.9, 821),
            });

        public static HeavyHexNutOption FindByDiameter(double diameterMm)
        {
            return Options
                .OrderBy(option => System.Math.Abs(option.DiameterMm - diameterMm))
                .FirstOrDefault();
        }
    }
}
