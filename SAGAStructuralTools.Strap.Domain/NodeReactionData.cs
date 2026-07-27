using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Strap.Domain
{
    public sealed class NodeReactionData
    {
        private NodeReactionData(
            string nodeId,
            Vector3D position,
            bool rotate90,
            IReadOnlyCollection<CombinationReaction> combinations,
            ConsolidatedReaction envelope)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new DomainValidationException("O número do nó é obrigatório.");

            NodeId = nodeId;
            Position = position;
            Rotate90 = rotate90;
            Combinations = combinations;
            Envelope = envelope;
        }

        public string NodeId { get; }
        public Vector3D Position { get; }
        public bool Rotate90 { get; }
        public IReadOnlyCollection<CombinationReaction> Combinations { get; }
        public ConsolidatedReaction Envelope { get; }
        public bool HasCompleteCombinationData => Combinations.Count > 0;

        public static NodeReactionData FromCombinations(
            string nodeId,
            Vector3D position,
            IEnumerable<CombinationReaction> combinations,
            bool rotate90 = false)
        {
            if (combinations == null)
                throw new DomainValidationException("As combinações do nó são obrigatórias.");

            CombinationReaction[] values = combinations.ToArray();
            if (values.Length == 0)
                throw new DomainValidationException(
                    "O nó deve possuir pelo menos uma reação por combinação.");
            if (values.Any(value => value == null))
                throw new DomainValidationException("Uma combinação não pode ser nula.");

            string duplicate = values
                .GroupBy(value => value.CombinationId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (duplicate != null)
                throw new DomainValidationException(
                    $"A combinação '{duplicate}' está duplicada no nó '{nodeId}'.");

            return new NodeReactionData(nodeId, position, rotate90, values, null);
        }

        public static NodeReactionData FromEnvelope(ConsolidatedReaction envelope)
        {
            if (envelope == null)
                throw new DomainValidationException("A envoltória do nó é obrigatória.");

            return new NodeReactionData(
                envelope.NodeId,
                Vector3D.Zero,
                false,
                Array.Empty<CombinationReaction>(),
                envelope);
        }
    }
}
