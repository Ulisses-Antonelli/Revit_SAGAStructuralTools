using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Strap.Domain
{
    public enum GroupingMethod
    {
        DirectSum,
        TransportedResultant
    }

    public sealed class GroupedReactionResult
    {
        internal GroupedReactionResult(
            string groupId,
            IReadOnlyCollection<CombinationReaction> combinations,
            ConsolidatedReaction envelope)
        {
            GroupId = groupId;
            Combinations = combinations;
            Envelope = envelope;
        }

        public string GroupId { get; }
        public IReadOnlyCollection<CombinationReaction> Combinations { get; }
        public ConsolidatedReaction Envelope { get; }
    }

    public static class GroupedReactionCalculator
    {
        public static GroupedReactionResult Calculate(
            string groupId,
            IEnumerable<NodeReactionData> nodes,
            GroupingMethod method,
            Vector3D referencePoint)
        {
            if (string.IsNullOrWhiteSpace(groupId))
                throw new DomainValidationException("O identificador do grupo é obrigatório.");
            if (nodes == null)
                throw new DomainValidationException("Os nós do grupo são obrigatórios.");
            if (!Enum.IsDefined(typeof(GroupingMethod), method))
                throw new DomainValidationException("O método de agrupamento é inválido.");

            NodeReactionData[] nodeValues = nodes.ToArray();
            if (nodeValues.Length < 2)
                throw new DomainValidationException(
                    "Uma base agrupada deve possuir pelo menos dois nós.");
            if (nodeValues.Any(node => node == null))
                throw new DomainValidationException("Um nó do grupo não pode ser nulo.");

            string duplicateNode = nodeValues
                .GroupBy(node => node.NodeId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (duplicateNode != null)
                throw new DomainValidationException(
                    $"O nó '{duplicateNode}' está duplicado no grupo.");

            NodeReactionData envelopeOnly = nodeValues
                .FirstOrDefault(node => !node.HasCompleteCombinationData);
            if (envelopeOnly != null)
                throw new DomainValidationException(
                    $"O nó '{envelopeOnly.NodeId}' possui apenas envoltórias. " +
                    "A soma rigorosa exige reações completas por combinação.");

            HashSet<string> expected = new HashSet<string>(
                nodeValues[0].Combinations.Select(item => item.CombinationId),
                StringComparer.Ordinal);
            foreach (NodeReactionData node in nodeValues.Skip(1))
            {
                var actual = new HashSet<string>(
                    node.Combinations.Select(item => item.CombinationId),
                    StringComparer.Ordinal);
                if (!expected.SetEquals(actual))
                    throw new DomainValidationException(
                        $"O nó '{node.NodeId}' não possui o mesmo conjunto de combinações do grupo.");
            }

            var totals = new List<CombinationReaction>();
            foreach (string combinationId in expected.OrderBy(value => value, StringComparer.Ordinal))
            {
                ForceMoment total = new ForceMoment(0m, 0m, 0m, 0m, 0m, 0m);
                foreach (NodeReactionData node in nodeValues)
                {
                    ForceMoment reaction = node.Combinations
                        .Single(item => item.CombinationId == combinationId)
                        .Reaction;
                    if (node.Rotate90)
                        reaction = reaction.Rotate90();
                    if (method == GroupingMethod.TransportedResultant)
                        reaction = reaction.Transport(node.Position, referencePoint);

                    total = total.Add(reaction);
                }

                totals.Add(new CombinationReaction(combinationId, total));
            }

            ConsolidatedReaction envelope = ReactionConsolidator.Consolidate(
                groupId,
                totals.Select(item => item.Reaction));
            return new GroupedReactionResult(groupId, totals, envelope);
        }
    }
}
