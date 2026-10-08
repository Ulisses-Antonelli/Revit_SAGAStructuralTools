using SAGAStructuralTools.Strap.Domain;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class ReactionWritePlanItem
    {
        internal ReactionWritePlanItem(
            StrapTargetProfile targetProfile,
            long elementId,
            string nodeId,
            ConsolidatedReaction reaction,
            ReactionValueSnapshot observedValues)
        {
            if (targetProfile == null)
                throw new ArgumentNullException(nameof(targetProfile));
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new ArgumentException("NO_PILAR é obrigatório.", nameof(nodeId));
            TargetProfileId = targetProfile.Id;
            ExpectedCategory = targetProfile.BuiltInCategory;
            ExpectedCategoryId = targetProfile.CategoryId;
            AssociationParameterName = targetProfile.AssociationParameterName;
            ReactionParameterNames = targetProfile.ReactionParameterNames.ToArray();
            ElementId = elementId;
            NodeId = nodeId;
            Reaction = reaction ?? throw new ArgumentNullException(nameof(reaction));
            ObservedValues = observedValues ??
                throw new ArgumentNullException(nameof(observedValues));
            DesiredValues = ReactionValueSnapshot.FromReaction(reaction);
        }

        internal long ElementId { get; }
        internal string TargetProfileId { get; }
        internal BuiltInCategory ExpectedCategory { get; }
        internal long ExpectedCategoryId { get; }
        internal string AssociationParameterName { get; }
        internal IReadOnlyCollection<string> ReactionParameterNames { get; }
        internal string NodeId { get; }
        internal ConsolidatedReaction Reaction { get; }
        internal ReactionValueSnapshot ObservedValues { get; }
        internal ReactionValueSnapshot DesiredValues { get; }
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
            if (Items.Select(item => item.TargetProfileId).Distinct(StringComparer.Ordinal).Count() != 1)
                throw new ArgumentException("O plano contém mais de um perfil de destino.", nameof(items));
        }

        internal IReadOnlyCollection<ReactionWritePlanItem> Items { get; }
    }
}
