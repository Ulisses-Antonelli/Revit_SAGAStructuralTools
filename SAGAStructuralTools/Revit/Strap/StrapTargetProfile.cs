using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class StrapTargetProfile
    {
        internal StrapTargetProfile(string id, string displayName, string entitySingular,
            string entityPlural, BuiltInCategory builtInCategory,
            string associationParameterName, IEnumerable<string> reactionParameterNames,
            bool feminine = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("O identificador do perfil é obrigatório.", nameof(id));
            Id = id;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            EntitySingular = entitySingular ?? throw new ArgumentNullException(nameof(entitySingular));
            EntityPlural = entityPlural ?? throw new ArgumentNullException(nameof(entityPlural));
            BuiltInCategory = builtInCategory;
            AssociationParameterName = associationParameterName ?? throw new ArgumentNullException(nameof(associationParameterName));
            ReactionParameterNames = (reactionParameterNames ?? throw new ArgumentNullException(nameof(reactionParameterNames))).ToArray();
            Feminine = feminine;
            if (ReactionParameterNames.Count != 7) throw new ArgumentException("O perfil deve declarar sete parâmetros de reação.", nameof(reactionParameterNames));
        }

        public string Id { get; }
        public string DisplayName { get; }
        internal string EntitySingular { get; }
        internal string EntityPlural { get; }
        internal BuiltInCategory BuiltInCategory { get; }
        internal long CategoryId => (long)BuiltInCategory;
        internal string AssociationParameterName { get; }
        internal IReadOnlyCollection<string> ReactionParameterNames { get; }
        internal bool Feminine { get; }
    }
}
