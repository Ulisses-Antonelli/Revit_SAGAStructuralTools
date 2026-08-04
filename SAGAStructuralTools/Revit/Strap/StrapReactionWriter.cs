using Autodesk.Revit.DB;
using SAGAStructuralTools.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SAGAStructuralTools.Revit.Strap
{
    internal sealed class StrapReactionWriter
    {
        private readonly RevitReactionParameterValidator _validator = new RevitReactionParameterValidator();

        internal ReactionWriteResult Write(Document document, ReactionWritePlan plan)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var elements = new Dictionary<long, Element>();
            var snapshots = new Dictionary<long, ReactionValueSnapshot>();
            var mappingConflicts = new List<ReactionWriteConflict>();
            foreach (var item in plan.Items)
            {
                Element element = document.GetElement(ToElementId(item.ElementId));
                StrapConnectionCandidate candidate = element == null ? null : _validator.ValidateCandidate(element);
                if (candidate == null || !candidate.IsValid || !string.Equals(candidate.NodeId, item.NodeId, StringComparison.Ordinal))
                {
                    mappingConflicts.Add(new ReactionWriteConflict(item.ElementId, item.NodeId, "<elemento>", double.NaN, double.NaN, double.NaN));
                    continue;
                }
                elements[item.ElementId] = element;
                snapshots[item.ElementId] = candidate.CurrentValues;
            }
            ReactionWritePreflight preflight = mappingConflicts.Count == 0
                ? ReactionWritePreflight.Evaluate(plan, snapshots)
                : new ReactionWritePreflight(Array.Empty<ReactionWriteOperation>(), mappingConflicts, 0);
            if (!preflight.CanStartTransaction)
            {
                foreach (var conflict in preflight.Conflicts)
                    SagaLog.Write($"Conflito STRAP: ElementId={conflict.ElementId}, NO_PILAR={conflict.NodeId}, parâmetro={conflict.ParameterName}, observado={Format(conflict.Observed)}, atual={Format(conflict.Current)}, novo={Format(conflict.Desired)}");
                return new ReactionWriteResult(ReactionWriteStatus.ValidationConflict, plan.Items.Count, 0, preflight.AlreadyUpdatedItemCount, 0, preflight.Conflicts, null);
            }
            if (preflight.Operations.Count == 0)
                return new ReactionWriteResult(ReactionWriteStatus.NoChanges, plan.Items.Count, 0, preflight.AlreadyUpdatedItemCount, 0, null, null);

            using (var transaction = new Transaction(document, "SAGA — Importar Reações STRAP"))
            {
                try
                {
                    transaction.Start();
                    foreach (var operation in preflight.Operations)
                    {
                        Element element = elements[operation.Item.ElementId];
                        string name = StrapParameterNames.Results[operation.ParameterIndex];
                        Parameter parameter = _validator.GetWritableResultParameter(element, name);
                        SagaLog.Write($"Gravação STRAP: ElementId={operation.Item.ElementId}, NO_PILAR={operation.Item.NodeId}, parâmetro={name}, anterior={Format(operation.Current)}, novo={Format(operation.Desired)}");
                        if (!parameter.Set(operation.Desired)) throw new InvalidOperationException($"Falha ao gravar {name}.");
                    }
                    if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("O Revit não confirmou a transação de importação.");
                    int updated = preflight.Operations.Select(x => x.Item.ElementId).Distinct().Count();
                    return new ReactionWriteResult(ReactionWriteStatus.Committed, plan.Items.Count, updated, preflight.AlreadyUpdatedItemCount, preflight.Operations.Count, null, null);
                }
                catch (Exception exception)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                    SagaLog.Exception("Rollback da gravação STRAP", exception);
                    return new ReactionWriteResult(ReactionWriteStatus.RolledBack, plan.Items.Count, 0, 0, 0, null, exception.ToString());
                }
            }
        }

        private static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
        private static ElementId ToElementId(long value)
        {
#if NET8_0_OR_GREATER
            return new ElementId(value);
#else
            if (value < int.MinValue || value > int.MaxValue) throw new InvalidOperationException("ElementId fora do intervalo do Revit 2023.");
            return new ElementId((int)value);
#endif
        }
    }
}
