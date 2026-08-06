using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal static class StrapTargetProfiles
    {
        internal static readonly StrapTargetProfile StructuralColumns = new StrapTargetProfile(
            "structural-columns", "Pilares estruturais", "pilar estrutural", "pilares estruturais",
            BuiltInCategory.OST_StructuralColumns, StrapParameterNames.Node, StrapParameterNames.Results);
        internal static readonly StrapTargetProfile StructuralConnections = new StrapTargetProfile(
            "structural-connections", "Conexões estruturais", "conexão estrutural", "conexões estruturais",
            BuiltInCategory.OST_StructConnections, StrapParameterNames.Node, StrapParameterNames.Results,
            feminine: true);
        internal static IReadOnlyCollection<StrapTargetProfile> All { get; } =
            new[] { StructuralColumns, StructuralConnections };
        internal static StrapTargetProfile Find(string id) => All.SingleOrDefault(
            profile => string.Equals(profile.Id, id, StringComparison.Ordinal));
    }
}
