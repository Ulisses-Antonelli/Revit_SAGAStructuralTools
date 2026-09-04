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
            });

        public static PlateThicknessOption FindByThickness(double thicknessMm)
        {
            return Options
                .OrderBy(option => System.Math.Abs(option.ThicknessMm - thicknessMm))
                .FirstOrDefault();
        }
    }
}
