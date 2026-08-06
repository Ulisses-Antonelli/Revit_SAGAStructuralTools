using Autodesk.Revit.DB;
using SAGAStructuralTools.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class RevitReactionParameterValidator
    {
        internal StrapTargetCandidate ValidateCandidate(Element element, StrapTargetProfile profile)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            long elementId = element.Id.GetId();
            var errors = new List<string>();
            if (element.Category == null || element.Category.Id.GetId() != profile.CategoryId)
                errors.Add($"O elemento {elementId} não pertence à categoria {profile.DisplayName}.");

            Parameter[] nodeParameters = element.GetParameters(profile.AssociationParameterName).ToArray();
            if (nodeParameters.Length == 0) return null;
            string nodeId = null;
            if (nodeParameters.Length != 1)
                errors.Add($"O elemento {elementId} deve possuir exatamente um parâmetro {profile.AssociationParameterName}.");
            else
            {
                Parameter parameter = nodeParameters[0];
                ValidateSharedInstanceParameter(element, parameter, profile.AssociationParameterName,
                    StorageType.String, SpecTypeId.String.Text, false, errors);
                if (parameter.StorageType == StorageType.String)
                {
                    nodeId = (parameter.AsString() ?? string.Empty).Trim();
                    if (nodeId.Length == 0)
                        errors.Add($"O parâmetro {profile.AssociationParameterName} do elemento {elementId} está vazio.");
                }
            }

            string[] resultNames = profile.ReactionParameterNames.ToArray();
            var currentValues = new double?[resultNames.Length];
            for (int index = 0; index < resultNames.Length; index++)
            {
                string name = resultNames[index];
                Parameter[] parameters = element.GetParameters(name).ToArray();
                if (parameters.Length != 1)
                {
                    errors.Add($"O elemento {elementId} não possui exatamente um parâmetro {name}.");
                    continue;
                }
                ValidateSharedInstanceParameter(element, parameters[0], name, StorageType.Double,
                    SpecTypeId.Number, true, errors);
                if (parameters[0].StorageType == StorageType.Double)
                    currentValues[index] = parameters[0].AsDouble();
            }

            return new StrapTargetCandidate(elementId, nodeId, errors,
                currentValues.All(value => value.HasValue)
                    ? new ReactionValueSnapshot(currentValues.Select(value => value.Value)) : null);
        }

        internal Parameter GetWritableResultParameter(Element element, string name)
        {
            Parameter[] parameters = element.GetParameters(name).ToArray();
            if (parameters.Length != 1)
                throw new InvalidOperationException($"O elemento {element.Id.GetId()} não possui exatamente um parâmetro {name}.");
            var errors = new List<string>();
            ValidateSharedInstanceParameter(element, parameters[0], name, StorageType.Double,
                SpecTypeId.Number, true, errors);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            return parameters[0];
        }

        private static void ValidateSharedInstanceParameter(Element owner, Parameter parameter,
            string name, StorageType storageType, ForgeTypeId dataType, bool requireWritable,
            ICollection<string> errors)
        {
            long elementId = owner.Id.GetId();
            if (parameter == null) { errors.Add($"O elemento {elementId} não possui o parâmetro {name}."); return; }
            if (!parameter.IsShared) errors.Add($"O parâmetro {name} do elemento {elementId} não é Shared Parameter.");
            if (parameter.Element == null || parameter.Element.Id.GetId() != elementId)
                errors.Add($"O parâmetro {name} do elemento {elementId} não é de instância.");
            if (parameter.StorageType != storageType)
                errors.Add($"O parâmetro {name} do elemento {elementId} possui StorageType incompatível.");
            if (parameter.Definition == null || parameter.Definition.GetDataType() != dataType)
                errors.Add($"O parâmetro {name} do elemento {elementId} possui Data Type incompatível.");
            if (requireWritable && parameter.IsReadOnly)
                errors.Add($"O parâmetro {name} do elemento {elementId} é somente leitura.");
        }
    }
}
