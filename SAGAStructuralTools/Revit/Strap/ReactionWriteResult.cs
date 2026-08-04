using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal enum ReactionWriteStatus
    {
        Committed,
        NoChanges,
        ValidationConflict,
        RolledBack
    }

    internal sealed class ReactionWriteConflict
    {
        internal ReactionWriteConflict(long elementId, string nodeId, string parameterName,
            double observed, double current, double desired)
        {
            ElementId = elementId;
            NodeId = nodeId;
            ParameterName = parameterName;
            Observed = observed;
            Current = current;
            Desired = desired;
        }

        internal long ElementId { get; }
        internal string NodeId { get; }
        internal string ParameterName { get; }
        internal double Observed { get; }
        internal double Current { get; }
        internal double Desired { get; }
    }

    internal sealed class ReactionWriteOperation
    {
        internal ReactionWriteOperation(ReactionWritePlanItem item, int parameterIndex,
            double current, double desired)
        {
            Item = item;
            ParameterIndex = parameterIndex;
            Current = current;
            Desired = desired;
        }

        internal ReactionWritePlanItem Item { get; }
        internal int ParameterIndex { get; }
        internal double Current { get; }
        internal double Desired { get; }
    }

    internal sealed class ReactionWritePreflight
    {
        internal ReactionWritePreflight(
            IEnumerable<ReactionWriteOperation> operations,
            IEnumerable<ReactionWriteConflict> conflicts,
            int alreadyUpdatedItems)
        {
            Operations = operations.ToArray();
            Conflicts = conflicts.ToArray();
            AlreadyUpdatedItems = alreadyUpdatedItems;
        }

        internal IReadOnlyCollection<ReactionWriteOperation> Operations { get; }
        internal IReadOnlyCollection<ReactionWriteConflict> Conflicts { get; }
        internal int AlreadyUpdatedItemCount => AlreadyUpdatedItems;
        internal int AlreadyUpdatedItems { get; }
        internal bool CanStartTransaction => Conflicts.Count == 0;

        internal static ReactionWritePreflight Evaluate(
            ReactionWritePlan plan,
            IReadOnlyDictionary<long, ReactionValueSnapshot> currentValues)
        {
            var operations = new List<ReactionWriteOperation>();
            var conflicts = new List<ReactionWriteConflict>();
            int alreadyUpdated = 0;
            foreach (ReactionWritePlanItem item in plan.Items)
            {
                if (!currentValues.TryGetValue(item.ElementId, out ReactionValueSnapshot current) ||
                    current == null)
                {
                    conflicts.Add(new ReactionWriteConflict(
                        item.ElementId, item.NodeId, "<elemento>", 0, 0, 0));
                    continue;
                }

                bool itemHasOperation = false;
                for (int index = 0; index < StrapParameterNames.Results.Length; index++)
                {
                    double observed = item.ObservedValues[index];
                    double actual = current[index];
                    double desired = item.DesiredValues[index];
                    if (ReactionNumberComparer.AreEquivalent(actual, desired))
                        continue;
                    if (ReactionNumberComparer.AreEquivalent(actual, observed))
                    {
                        operations.Add(new ReactionWriteOperation(item, index, actual, desired));
                        itemHasOperation = true;
                        continue;
                    }
                    conflicts.Add(new ReactionWriteConflict(item.ElementId, item.NodeId,
                        StrapParameterNames.Results[index], observed, actual, desired));
                }
                if (!itemHasOperation && !conflicts.Any(value => value.ElementId == item.ElementId))
                    alreadyUpdated++;
            }
            return new ReactionWritePreflight(operations, conflicts, alreadyUpdated);
        }
    }

    internal sealed class ReactionWriteResult
    {
        internal ReactionWriteResult(ReactionWriteStatus status, int requestedItems,
            int updatedItems, int alreadyUpdatedItems, int parameterWrites,
            IEnumerable<ReactionWriteConflict> conflicts, string error)
        {
            Status = status;
            RequestedItems = requestedItems;
            UpdatedItems = updatedItems;
            AlreadyUpdatedItems = alreadyUpdatedItems;
            ParameterWrites = parameterWrites;
            Conflicts = (conflicts ?? Array.Empty<ReactionWriteConflict>()).ToArray();
            Error = error;
        }

        internal ReactionWriteStatus Status { get; }
        internal int RequestedItems { get; }
        internal int UpdatedItems { get; }
        internal int AlreadyUpdatedItems { get; }
        internal int ParameterWrites { get; }
        internal IReadOnlyCollection<ReactionWriteConflict> Conflicts { get; }
        internal string Error { get; }
    }
}
