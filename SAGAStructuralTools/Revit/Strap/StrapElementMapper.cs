using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class StrapElementMapper
    {
        private readonly RevitReactionParameterValidator _validator =
            new RevitReactionParameterValidator();

        internal IReadOnlyCollection<StrapConnectionCandidate> Collect(Document document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            return new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_StructConnections)
                .WhereElementIsNotElementType()
                .Select(element => _validator.ValidateCandidate(element))
                .Where(candidate => candidate != null)
                .ToArray();
        }
    }
}
