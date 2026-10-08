using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class StrapTargetMatch
    {
        internal StrapTargetMatch(IEnumerable<StrapTargetCandidate> candidates, string error)
        {
            Candidates = (candidates ?? Array.Empty<StrapTargetCandidate>()).ToArray();
            Error = error;
        }
        internal IReadOnlyCollection<StrapTargetCandidate> Candidates { get; }
        internal string Error { get; }
        internal bool IsMissing => Candidates.Count == 0;
        internal bool IsDuplicate => Candidates.Count > 1;
    }

    internal static class StrapTargetMapping
    {
        internal static StrapTargetMatch Match(StrapTargetProfile profile, string nodeId,
            IEnumerable<StrapTargetCandidate> candidates)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var matches = (candidates ?? Array.Empty<StrapTargetCandidate>())
                .Where(candidate => string.Equals(candidate.NodeId, nodeId, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
                return new StrapTargetMatch(matches,
                    $"{(profile.Feminine ? "Nenhuma" : "Nenhum")} {profile.EntitySingular} corresponde ao {profile.AssociationParameterName} “{nodeId}”.");
            if (matches.Length > 1)
                return new StrapTargetMatch(matches,
                    $"Mais de {(profile.Feminine ? "uma" : "um")} {profile.EntitySingular} possui {profile.AssociationParameterName} “{nodeId}”.");
            return new StrapTargetMatch(matches, null);
        }
    }
}
