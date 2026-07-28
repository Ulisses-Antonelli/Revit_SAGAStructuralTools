using Autodesk.Revit.DB;
using SAGAStructuralTools.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class RevitReactionParameterValidator
    {
        internal StrapConnectionCandidate ValidateCandidate(Element element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            var categoryErrors = new List<string>();
            if (element.Category == null ||
                element.Category.Id.GetId() !=
                (long)BuiltInCategory.OST_StructConnections)
            {
                categoryErrors.Add("O elemento não pertence a OST_StructConnections.");
            }

            Parameter[] nodeParameters = element
                .GetParameters(StrapParameterNames.Node)
                .ToArray();
            if (nodeParameters.Length == 0)
                return null;

            var errors = categoryErrors;
            string nodeId = null;
            if (nodeParameters.Length != 1)
            {
                errors.Add("Deve existir exatamente um parâmetro SGA_NO_PILAR.");
            }
            else
            {
                Parameter parameter = nodeParameters[0];
                ValidateSharedInstanceParameter(
                    element,
                    parameter,
                    StrapParameterNames.Node,
                    StorageType.String,
                    SpecTypeId.String.Text,
                    requireWritable: false,
                    errors);
                if (parameter.StorageType == StorageType.String)
                {
                    nodeId = (parameter.AsString() ?? string.Empty).Trim();
                    if (nodeId.Length == 0)
                        errors.Add("SGA_NO_PILAR está vazio.");
                }
            }

            foreach (string name in StrapParameterNames.Results)
            {
                Parameter[] parameters = element.GetParameters(name).ToArray();
                if (parameters.Length != 1)
                {
                    errors.Add($"Deve existir exatamente um parâmetro {name}.");
                    continue;
                }
                ValidateSharedInstanceParameter(
                    element,
                    parameters[0],
                    name,
                    StorageType.Double,
                    SpecTypeId.Number,
                    requireWritable: true,
                    errors);
            }

            return new StrapConnectionCandidate(
                element.Id.GetId(),
                nodeId,
                errors);
        }

        internal Parameter GetWritableResultParameter(Element element, string name)
        {
            Parameter[] parameters = element.GetParameters(name).ToArray();
            if (parameters.Length != 1)
                throw new InvalidOperationException(
                    $"O elemento não possui exatamente um parâmetro {name}.");
            var errors = new List<string>();
            ValidateSharedInstanceParameter(
                element,
                parameters[0],
                name,
                StorageType.Double,
                SpecTypeId.Number,
                requireWritable: true,
                errors);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", errors));
            return parameters[0];
        }

        private static void ValidateSharedInstanceParameter(
            Element owner,
            Parameter parameter,
            string name,
            StorageType storageType,
            ForgeTypeId dataType,
            bool requireWritable,
            ICollection<string> errors)
        {
            if (parameter == null)
            {
                errors.Add($"{name} está ausente.");
                return;
            }
            if (!parameter.IsShared)
                errors.Add($"{name} não é Shared Parameter.");
            if (parameter.Element == null ||
                parameter.Element.Id.GetId() != owner.Id.GetId())
            {
                errors.Add($"{name} não é parâmetro de instância do elemento.");
            }
            if (parameter.StorageType != storageType)
                errors.Add($"{name} possui StorageType incompatível.");
            if (parameter.Definition == null ||
                parameter.Definition.GetDataType() != dataType)
            {
                errors.Add($"{name} possui Data Type incompatível.");
            }
            if (requireWritable && parameter.IsReadOnly)
                errors.Add($"{name} é somente leitura.");
        }
    }
}
