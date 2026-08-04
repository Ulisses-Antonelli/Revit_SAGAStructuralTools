using SAGAStructuralTools.Strap.Domain;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal static class ReactionNumberComparer
    {
        internal const double AbsoluteTolerance = 1e-9;

        internal static bool AreEquivalent(double current, double expected)
            => Math.Abs(current - expected) <= AbsoluteTolerance;
    }

    internal sealed class ReactionParameterComparison
    {
        internal ReactionParameterComparison(string name, double current, double desired)
        {
            Name = name;
            Current = current;
            Desired = desired;
        }

        public string Name { get; }
        public double Current { get; }
        public double Desired { get; }
        public bool IsDifferent => !ReactionNumberComparer.AreEquivalent(Current, Desired);
        public string Display =>
            Current.ToString("G17", CultureInfo.CurrentCulture) + " → " +
            Desired.ToString("G17", CultureInfo.CurrentCulture);
    }

    internal sealed class ReactionValueSnapshot
    {
        private readonly double[] _values;

        internal ReactionValueSnapshot(IEnumerable<double> values)
        {
            _values = (values ?? throw new ArgumentNullException(nameof(values))).ToArray();
            if (_values.Length != StrapParameterNames.Results.Length)
                throw new ArgumentException("O snapshot deve possuir sete valores.", nameof(values));
        }

        internal static ReactionValueSnapshot FromReaction(ConsolidatedReaction reaction)
        {
            if (reaction == null)
                throw new ArgumentNullException(nameof(reaction));
            return new ReactionValueSnapshot(new[]
            {
                (double)reaction.X1,
                (double)reaction.X2,
                (double)reaction.X3Max,
                (double)reaction.X3Min,
                (double)reaction.X4,
                (double)reaction.X5,
                (double)reaction.X6
            });
        }

        internal double this[int index] => _values[index];
        internal double Get(string parameterName)
        {
            int index = Array.IndexOf(StrapParameterNames.Results, parameterName);
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(parameterName));
            return _values[index];
        }

        internal bool IsEquivalentTo(ReactionValueSnapshot other)
            => other != null && Enumerable.Range(0, _values.Length)
                .All(index => ReactionNumberComparer.AreEquivalent(_values[index], other[index]));

        internal IReadOnlyCollection<ReactionParameterComparison> CompareTo(
            ReactionValueSnapshot desired)
        {
            if (desired == null)
                throw new ArgumentNullException(nameof(desired));
            return Enumerable.Range(0, _values.Length)
                .Select(index => new ReactionParameterComparison(
                    StrapParameterNames.Results[index], _values[index], desired[index]))
                .ToArray();
        }
    }
}
