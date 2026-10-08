using System.Linq;
using Xunit;

namespace SAGAStructuralTools.Strap.Domain.Tests
{
    public sealed class GroupedReactionCalculatorTests
    {
        [Fact]
        public void RotationIsAppliedBeforeCombinationSum()
        {
            NodeReactionData first = Node("1", false, C("C1", fx: 2m, fy: 3m));
            NodeReactionData second = Node("2", true, C("C1", fx: 5m, fy: 7m));

            GroupedReactionResult result = Calculate(first, second);
            ForceMoment total = result.Combinations.Single().Reaction;

            Assert.Equal(9m, total.Fx);
            Assert.Equal(8m, total.Fy);
        }

        [Fact]
        public void ReactionsAreSummedIndependentlyByCombination()
        {
            NodeReactionData first = Node(
                "1",
                false,
                C("C1", fx: 10m),
                C("C2", fx: -4m));
            NodeReactionData second = Node(
                "2",
                false,
                C("C1", fx: 2m),
                C("C2", fx: 1m));

            GroupedReactionResult result = Calculate(first, second);

            Assert.Equal(12m, Get(result, "C1").Fx);
            Assert.Equal(-3m, Get(result, "C2").Fx);
            Assert.Equal(12m, result.Envelope.X1);
        }

        [Fact]
        public void MissingCombinationInOneNode_IsRejected()
        {
            NodeReactionData first = Node("1", false, C("C1"), C("C2"));
            NodeReactionData second = Node("2", false, C("C1"));

            Assert.Throws<DomainValidationException>(() => Calculate(first, second));
        }

        [Fact]
        public void OppositeForcesAtTwoSupports_CancelInDirectSum()
        {
            NodeReactionData first = Node("1", false, C("C1", fx: 10m));
            NodeReactionData second = Node("2", false, C("C1", fx: -10m));

            ForceMoment total = Calculate(first, second).Combinations.Single().Reaction;

            Assert.Equal(0m, total.Fx);
            Assert.Equal(0m, total.Fy);
            Assert.Equal(0m, total.Fz);
        }

        [Fact]
        public void HorizontalForceTransport_AddsRxFToMoment()
        {
            NodeReactionData first = NodeAt(
                "1",
                new Vector3D(0m, 0m, 2m),
                C("C1", fx: 3m));
            NodeReactionData second = NodeAt("2", Vector3D.Zero, C("C1"));

            ForceMoment total = CalculateTransported(first, second).Combinations.Single().Reaction;

            Assert.Equal(0m, total.Mx);
            Assert.Equal(6m, total.My);
            Assert.Equal(0m, total.Mz);
        }

        [Fact]
        public void VerticalForceTransport_AddsRxFToMoment()
        {
            NodeReactionData first = NodeAt(
                "1",
                new Vector3D(2m, 0m, 0m),
                C("C1", fz: 5m));
            NodeReactionData second = NodeAt("2", Vector3D.Zero, C("C1"));

            ForceMoment total = CalculateTransported(first, second).Combinations.Single().Reaction;

            Assert.Equal(0m, total.Mx);
            Assert.Equal(-10m, total.My);
            Assert.Equal(0m, total.Mz);
        }

        [Fact]
        public void SupportAtReferencePoint_DoesNotChangeMoment()
        {
            var reference = new Vector3D(1m, 2m, 3m);
            NodeReactionData first = NodeAt(
                "1",
                reference,
                C("C1", fx: 2m, fy: 3m, fz: 4m, mx: 5m, my: 6m, mz: 7m));
            NodeReactionData second = NodeAt("2", reference, C("C1"));

            GroupedReactionResult result = GroupedReactionCalculator.Calculate(
                "BASE",
                new[] { first, second },
                GroupingMethod.TransportedResultant,
                reference);
            ForceMoment total = result.Combinations.Single().Reaction;

            Assert.Equal(5m, total.Mx);
            Assert.Equal(6m, total.My);
            Assert.Equal(7m, total.Mz);
        }

        [Fact]
        public void GroupSupportsMoreThanTwoNodes()
        {
            GroupedReactionResult result = Calculate(
                Node("1", false, C("C1", fz: 1m)),
                Node("2", false, C("C1", fz: 2m)),
                Node("3", false, C("C1", fz: 3m)));

            Assert.Equal(6m, result.Combinations.Single().Reaction.Fz);
        }

        [Fact]
        public void RigorousGroupingWithEnvelopeOnlyNode_IsRejected()
        {
            var envelope = new ConsolidatedReaction("1", 1m, 1m, 2m, -2m, 1m, 1m, 1m);
            NodeReactionData first = NodeReactionData.FromEnvelope(envelope);
            NodeReactionData second = Node("2", false, C("C1"));

            Assert.Throws<DomainValidationException>(() => Calculate(first, second));
        }

        [Fact]
        public void GroupEnvelopePreservesPositiveAndNegativeFz()
        {
            NodeReactionData first = Node(
                "1",
                false,
                C("C1", fz: 5m),
                C("C2", fz: -3m));
            NodeReactionData second = Node(
                "2",
                false,
                C("C1", fz: 2m),
                C("C2", fz: -4m));

            GroupedReactionResult result = Calculate(first, second);

            Assert.Equal(7m, result.Envelope.X3Max);
            Assert.Equal(-7m, result.Envelope.X3Min);
        }

        private static GroupedReactionResult Calculate(params NodeReactionData[] nodes)
            => GroupedReactionCalculator.Calculate(
                "BASE",
                nodes,
                GroupingMethod.DirectSum,
                Vector3D.Zero);

        private static GroupedReactionResult CalculateTransported(
            params NodeReactionData[] nodes)
            => GroupedReactionCalculator.Calculate(
                "BASE",
                nodes,
                GroupingMethod.TransportedResultant,
                Vector3D.Zero);

        private static NodeReactionData Node(
            string id,
            bool rotate90,
            params CombinationReaction[] combinations)
            => NodeReactionData.FromCombinations(
                id,
                Vector3D.Zero,
                combinations,
                rotate90);

        private static NodeReactionData NodeAt(
            string id,
            Vector3D position,
            params CombinationReaction[] combinations)
            => NodeReactionData.FromCombinations(id, position, combinations);

        private static CombinationReaction C(
            string id,
            decimal fx = 0m,
            decimal fy = 0m,
            decimal fz = 0m,
            decimal mx = 0m,
            decimal my = 0m,
            decimal mz = 0m)
            => new CombinationReaction(
                id,
                new ForceMoment(fx, fy, fz, mx, my, mz));

        private static ForceMoment Get(GroupedReactionResult result, string combination)
            => result.Combinations.Single(item => item.CombinationId == combination).Reaction;
    }
}
