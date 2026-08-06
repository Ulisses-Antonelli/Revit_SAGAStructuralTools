using System;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal static class ReactionWriteTargetGuard
    {
        internal static bool MatchesProfile(ReactionWritePlanItem item, StrapTargetProfile profile)
            => item != null && profile != null &&
               string.Equals(item.TargetProfileId, profile.Id, StringComparison.Ordinal) &&
               item.ExpectedCategoryId == profile.CategoryId &&
               string.Equals(item.AssociationParameterName, profile.AssociationParameterName, StringComparison.Ordinal) &&
               item.ReactionParameterNames.SequenceEqual(profile.ReactionParameterNames, StringComparer.Ordinal);
        internal static bool MatchesCategory(ReactionWritePlanItem item, long actualCategoryId)
            => item != null && item.ExpectedCategoryId == actualCategoryId;
    }
}
