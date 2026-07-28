using Autodesk.Revit.DB;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Strap.Domain;
using System;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class StrapReactionWriter
    {
        private readonly RevitReactionParameterValidator _validator =
            new RevitReactionParameterValidator();

        internal void Write(Document document, ReactionWritePlan plan)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            var targets = plan.Items.Select(item =>
            {
                Element element = document.GetElement(ToElementId(item.ElementId));
                StrapConnectionCandidate candidate =
                    element == null ? null : _validator.ValidateCandidate(element);
                if (candidate == null ||
                    !candidate.IsValid ||
                    !string.Equals(candidate.NodeId, item.NodeId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"A base do nó '{item.NodeId}' mudou após a prévia.");
                }
                return new { Item = item, Element = element };
            }).ToArray();

            using (var transaction = new Transaction(
                document,
                "SAGA — Importar Reações STRAP"))
            {
                transaction.Start();
                try
                {
                    foreach (var target in targets)
                        SetValues(target.Element, target.Item.Reaction);
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException(
                            "O Revit não confirmou a transação de importação.");
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw;
                }
            }
        }

        private void SetValues(Element element, ConsolidatedReaction reaction)
        {
            double[] values =
            {
                (double)reaction.X1,
                (double)reaction.X2,
                (double)reaction.X3Max,
                (double)reaction.X3Min,
                (double)reaction.X4,
                (double)reaction.X5,
                (double)reaction.X6
            };
            for (int index = 0; index < StrapParameterNames.Results.Length; index++)
            {
                Parameter parameter = _validator.GetWritableResultParameter(
                    element,
                    StrapParameterNames.Results[index]);
                if (!parameter.Set(values[index]))
                    throw new InvalidOperationException(
                        $"Falha ao gravar {StrapParameterNames.Results[index]}.");
            }
        }

        private static ElementId ToElementId(long value)
        {
#if NET8_0_OR_GREATER
            return new ElementId(value);
#else
            if (value < int.MinValue || value > int.MaxValue)
                throw new InvalidOperationException("ElementId fora do intervalo do Revit 2023.");
            return new ElementId((int)value);
#endif
        }
    }
}
