using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Strap.Domain
{
    public static class ReactionConsolidator
    {
        public static ConsolidatedReaction ConsolidateNode(
            string nodeId,
            ForceMoment maximumRow,
            ForceMoment minimumRow,
            bool rotate90 = false)
        {
            if (maximumRow == null)
                throw new DomainValidationException("A linha Máx do nó é obrigatória.");
            if (minimumRow == null)
                throw new DomainValidationException("A linha Mín do nó é obrigatória.");

            ConsolidatedReaction result = Consolidate(
                nodeId,
                new[] { maximumRow, minimumRow });
            return rotate90 ? result.Rotate90() : result;
        }

        public static ConsolidatedReaction Consolidate(
            string nodeId,
            IEnumerable<ForceMoment> reactions)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new DomainValidationException("O número do nó é obrigatório.");
            if (reactions == null)
                throw new DomainValidationException("As reações do nó são obrigatórias.");

            ForceMoment[] values = reactions.ToArray();
            if (values.Length == 0)
                throw new DomainValidationException(
                    "É necessária pelo menos uma reação para calcular a envoltória.");
            if (values.Any(value => value == null))
                throw new DomainValidationException("Uma reação não pode ser nula.");

            return new ConsolidatedReaction(
                nodeId,
                values.Max(value => Math.Abs(value.Fx)),
                values.Max(value => Math.Abs(value.Fy)),
                values.Max(value => value.Fz),
                values.Min(value => value.Fz),
                values.Max(value => Math.Abs(value.Mx)),
                values.Max(value => Math.Abs(value.My)),
                values.Max(value => Math.Abs(value.Mz)));
        }
    }
}
