using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class StrapElementMapper
    {
        private readonly RevitReactionParameterValidator _validator = new RevitReactionParameterValidator();

        internal StrapCandidateCatalog CollectAll(Document document, IEnumerable<StrapTargetProfile> profiles)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return new StrapCandidateCatalog((profiles ?? throw new ArgumentNullException(nameof(profiles)))
                .Select(profile => new KeyValuePair<string, IReadOnlyCollection<StrapTargetCandidate>>(
                    profile.Id, Collect(document, profile))));
        }

        internal IReadOnlyCollection<StrapTargetCandidate> Collect(Document document, StrapTargetProfile profile)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            return new FilteredElementCollector(document)
                .OfCategory(profile.BuiltInCategory)
                .WhereElementIsNotElementType()
                .Select(element => _validator.ValidateCandidate(element, profile))
                .Where(candidate => candidate != null)
                .ToArray();
        }
    }
}
