using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SAGAStructuralTools.BasePlate.Domain
{
    public static class ConcreteStrengthCatalog
    {
        public static IReadOnlyList<ConcreteStrengthOption> Options { get; } =
            new ReadOnlyCollection<ConcreteStrengthOption>(new[]
            {
                new ConcreteStrengthOption("C-20", 20.0),
                new ConcreteStrengthOption("C-25", 25.0),
                new ConcreteStrengthOption("C-30", 30.0),
                new ConcreteStrengthOption("C-35", 35.0),
            });

        public static ConcreteStrengthOption FindByFck(double fckMpa)
        {
            return Options
                .OrderBy(option => System.Math.Abs(option.FckMpa - fckMpa))
                .FirstOrDefault();
        }
    }
}
