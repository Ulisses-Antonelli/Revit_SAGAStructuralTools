using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class StrapTargetCandidate
    {
        internal StrapTargetCandidate(long elementId, string nodeId, IEnumerable<string> errors,
            ReactionValueSnapshot currentValues = null)
        {
            ElementId = elementId;
            NodeId = nodeId;
            Errors = (errors ?? Array.Empty<string>()).ToArray();
            CurrentValues = currentValues;
        }
        internal long ElementId { get; }
        internal string NodeId { get; }
        internal IReadOnlyCollection<string> Errors { get; }
        internal ReactionValueSnapshot CurrentValues { get; }
        internal bool IsValid => !string.IsNullOrEmpty(NodeId) && Errors.Count == 0 && CurrentValues != null;
    }
}
