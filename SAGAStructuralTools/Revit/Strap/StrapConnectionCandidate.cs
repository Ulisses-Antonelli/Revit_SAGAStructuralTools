using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class StrapConnectionCandidate
    {
        internal StrapConnectionCandidate(
            long elementId,
            string nodeId,
            IEnumerable<string> errors)
        {
            ElementId = elementId;
            NodeId = nodeId;
            Errors = (errors ?? Array.Empty<string>()).ToArray();
        }

        internal long ElementId { get; }
        internal string NodeId { get; }
        internal IReadOnlyCollection<string> Errors { get; }
        internal bool IsValid =>
            !string.IsNullOrEmpty(NodeId) && Errors.Count == 0;
    }
}
