using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class StrapCandidateCatalog
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyCollection<StrapTargetCandidate>> _entries;
        internal StrapCandidateCatalog(IEnumerable<KeyValuePair<string, IReadOnlyCollection<StrapTargetCandidate>>> entries)
        {
            _entries = (entries ?? throw new ArgumentNullException(nameof(entries)))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        }
        internal IReadOnlyCollection<StrapTargetCandidate> Get(StrapTargetProfile profile)
            => profile != null && _entries.TryGetValue(profile.Id, out var candidates)
                ? candidates : Array.Empty<StrapTargetCandidate>();
    }
}
