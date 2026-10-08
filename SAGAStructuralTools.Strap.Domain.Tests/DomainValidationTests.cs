using Xunit;

namespace SAGAStructuralTools.Strap.Domain.Tests
{
    public sealed class DomainValidationTests
    {
        [Fact]
        public void ConsolidatedReactionRejectsNegativeModuleResult()
        {
            Assert.Throws<DomainValidationException>(
                () => new ConsolidatedReaction("1", -1m, 0m, 0m, 0m, 0m, 0m, 0m));
        }

        [Fact]
        public void ConsolidatedReactionRejectsInvertedFzEnvelope()
        {
            Assert.Throws<DomainValidationException>(
                () => new ConsolidatedReaction("1", 0m, 0m, -2m, 3m, 0m, 0m, 0m));
        }

        [Fact]
        public void DuplicateCombinationInNodeIsRejected()
        {
            var first = new CombinationReaction("C1", Zero());
            var duplicate = new CombinationReaction("C1", Zero());

            Assert.Throws<DomainValidationException>(
                () => NodeReactionData.FromCombinations(
                    "1",
                    Vector3D.Zero,
                    new[] { first, duplicate }));
        }

        [Fact]
        public void GroupWithFewerThanTwoNodesIsRejected()
        {
            NodeReactionData node = NodeReactionData.FromCombinations(
                "1",
                Vector3D.Zero,
                new[] { new CombinationReaction("C1", Zero()) });

            Assert.Throws<DomainValidationException>(
                () => GroupedReactionCalculator.Calculate(
                    "BASE",
                    new[] { node },
                    GroupingMethod.DirectSum,
                    Vector3D.Zero));
        }

        [Fact]
        public void DuplicateNodeInGroupIsRejected()
        {
            NodeReactionData first = NodeReactionData.FromCombinations(
                "1",
                Vector3D.Zero,
                new[] { new CombinationReaction("C1", Zero()) });
            NodeReactionData duplicate = NodeReactionData.FromCombinations(
                "1",
                Vector3D.Zero,
                new[] { new CombinationReaction("C1", Zero()) });

            Assert.Throws<DomainValidationException>(
                () => GroupedReactionCalculator.Calculate(
                    "BASE",
                    new[] { first, duplicate },
                    GroupingMethod.DirectSum,
                    Vector3D.Zero));
        }

        private static ForceMoment Zero()
            => new ForceMoment(0m, 0m, 0m, 0m, 0m, 0m);
    }
}
