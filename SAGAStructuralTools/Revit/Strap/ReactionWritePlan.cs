using SAGAStructuralTools.Strap.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class ReactionWritePlanItem
    {
        internal ReactionWritePlanItem(
            long elementId,
            string nodeId,
            ConsolidatedReaction reaction)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new ArgumentException("NO_PILAR é obrigatório.", nameof(nodeId));
            ElementId = elementId;
            NodeId = nodeId;
            Reaction = reaction ?? throw new ArgumentNullException(nameof(reaction));
        }

        internal long ElementId { get; }
        internal string NodeId { get; }
        internal ConsolidatedReaction Reaction { get; }
    }

    internal sealed class ReactionWritePlan
    {
        internal ReactionWritePlan(IEnumerable<ReactionWritePlanItem> items)
        {
            Items = (items ?? throw new ArgumentNullException(nameof(items))).ToArray();
            if (Items.Count == 0)
                throw new ArgumentException("Selecione ao menos uma linha.", nameof(items));
            if (Items.GroupBy(item => item.ElementId).Any(group => group.Count() > 1))
                throw new ArgumentException("O plano contém elementos repetidos.", nameof(items));
        }

        internal IReadOnlyCollection<ReactionWritePlanItem> Items { get; }
    }
}
