using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SAGAStructuralTools.BasePlate.Domain
{
    public static class PlateThicknessCatalog
    {
        public static IReadOnlyList<PlateThicknessOption> Options { get; } =
            new ReadOnlyCollection<PlateThicknessOption>(new[]
            {
                new PlateThicknessOption("1/4\" (6,35 mm)", 6.35),
                new PlateThicknessOption("5/16\" (8,00 mm)", 8.00),
                new PlateThicknessOption("3/8\" (9,50 mm)", 9.50),
                new PlateThicknessOption("1/2\" (12,70 mm)", 12.70),
                new PlateThicknessOption("5/8\" (16,00 mm)", 16.00),
                new PlateThicknessOption("3/4\" (19,00 mm)", 19.00),
                new PlateThicknessOption("7/8\" (22,00 mm)", 22.00),
                new PlateThicknessOption("1\" (25,40 mm)", 25.40),
                new PlateThicknessOption("1.1/8\" (28,58 mm)", 28.58),
                new PlateThicknessOption("1.1/4\" (31,75 mm)", 31.75),
                new PlateThicknessOption("1.3/8\" (34,93 mm)", 34.93),
                new PlateThicknessOption("1.1/2\" (38,10 mm)", 38.10),
                new PlateThicknessOption("1.5/8\" (41,28 mm)", 41.28),
                new PlateThicknessOption("1.3/4\" (44,45 mm)", 44.45),
                new PlateThicknessOption("1.7/8\" (47,63 mm)", 47.63),
                new PlateThicknessOption("2\" (50,80 mm)", 50.80),
                new PlateThicknessOption("2.1/4\" (57,15 mm)", 57.15),
                new PlateThicknessOption("2.1/2\" (63,50 mm)", 63.50),
                new PlateThicknessOption("2.3/4\" (69,85 mm)", 69.85),
                new PlateThicknessOption("3\" (76,20 mm)", 76.20),
            });

        public static PlateThicknessOption FindByThickness(double thicknessMm)
        {
            return Options
                .OrderBy(option => System.Math.Abs(option.ThicknessMm - thicknessMm))
                .FirstOrDefault();
        }
    }
}
