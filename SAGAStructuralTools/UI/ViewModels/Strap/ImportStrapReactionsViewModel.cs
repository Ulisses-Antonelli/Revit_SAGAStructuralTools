using SAGAStructuralTools.Revit.Strap;
using SAGAStructuralTools.Strap.Application;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SAGAStructuralTools.UI.ViewModels.Strap
{
    internal sealed class ImportStrapReactionsViewModel : ViewModelBase
    {
        internal ImportStrapReactionsViewModel(
            string filePath,
            StrapPartialImportResult import,
            IEnumerable<StrapConnectionCandidate> candidates)
        {
            FilePath = filePath;
            ForceUnit = string.Join(", ", import.ParseResult.ForceUnits);
            MomentUnit = string.Join(", ", import.ParseResult.MomentUnits);
            GlobalErrors = import.GlobalDiagnostics
                .Where(item => item.Severity == DiagnosticSeverity.Error)
                .Select(item => item.Message)
                .ToArray();
            Rows = new ObservableCollection<StrapNodePreviewItem>(
                BuildRows(import, candidates));
            foreach (StrapNodePreviewItem row in Rows)
                row.SelectionChanged += (sender, args) => RaiseCounts();
        }

        public string FilePath { get; }
        public string ForceUnit { get; }
        public string MomentUnit { get; }
        public string UnitNotice =>
            "Os valores serão armazenados convencionalmente como Number: " +
            "forças em tf e momentos em tf·m, sem conversão pelo Revit.";
        public IReadOnlyCollection<string> GlobalErrors { get; }
        public ObservableCollection<StrapNodePreviewItem> Rows { get; }
        public int FoundCount => Rows.Count;
        public int ValidCount => Rows.Count(row => row.IsValid);
        public int SelectedCount => Rows.Count(row => row.IsValid && row.IsSelected);
        public int InvalidCount => Rows.Count(row => !row.IsValid);
        public int DeselectedValidCount =>
            Rows.Count(row => row.IsValid && !row.IsSelected);
        public bool CanConfirm => GlobalErrors.Count == 0 && SelectedCount > 0;
        public string ConfirmText => $"Importar {SelectedCount} linha(s)";

        internal ReactionWritePlan CreatePlan()
        {
            if (!CanConfirm)
                throw new InvalidOperationException("Nenhuma linha válida foi selecionada.");
            return new ReactionWritePlan(
                Rows.Where(row => row.IsValid && row.IsSelected)
                    .Select(row => row.ToPlanItem()));
        }

        private static IEnumerable<StrapNodePreviewItem> BuildRows(
            StrapPartialImportResult import,
            IEnumerable<StrapConnectionCandidate> sourceCandidates)
        {
            StrapConnectionCandidate[] candidates =
                (sourceCandidates ?? Array.Empty<StrapConnectionCandidate>()).ToArray();
            var used = new HashSet<long>();
            foreach (StrapNodeImportResult node in import.Nodes)
            {
                StrapConnectionCandidate[] matches = candidates
                    .Where(candidate => string.Equals(
                        candidate.NodeId,
                        node.NodeId,
                        StringComparison.Ordinal))
                    .ToArray();
                var errors = node.Diagnostics
                    .Where(item =>
                        item.Severity == DiagnosticSeverity.Error ||
                        item.Code == DiagnosticCodes.DuplicateNodeIdentical)
                    .Select(item => item.Message)
                    .ToList();
                if (node.Reaction == null && errors.Count == 0)
                    errors.Add("Linha bloqueada por um erro global do documento.");
                long? elementId = null;
                if (matches.Length == 0)
                {
                    errors.Add("Nenhuma conexão corresponde ao SGA_NO_PILAR.");
                    yield return new StrapNodePreviewItem(
                        node.NodeId,
                        null,
                        node.Node.Declaration?.Trace.LineNumber,
                        node.Reaction,
                        errors);
                }
                else if (matches.Length > 1)
                {
                    string duplicate =
                        "Mais de uma conexão possui o mesmo SGA_NO_PILAR.";
                    foreach (StrapConnectionCandidate match in matches)
                    {
                        used.Add(match.ElementId);
                        yield return new StrapNodePreviewItem(
                            node.NodeId,
                            match.ElementId,
                            node.Node.Declaration?.Trace.LineNumber,
                            node.Reaction,
                            errors.Concat(match.Errors).Concat(new[] { duplicate }));
                    }
                }
                else
                {
                    elementId = matches[0].ElementId;
                    used.Add(matches[0].ElementId);
                    errors.AddRange(matches[0].Errors);
                    yield return new StrapNodePreviewItem(
                        node.NodeId,
                        elementId,
                        node.Node.Declaration?.Trace.LineNumber,
                        node.Reaction,
                        errors);
                }
            }

            foreach (StrapConnectionCandidate candidate in candidates
                .Where(candidate => !used.Contains(candidate.ElementId)))
            {
                var errors = candidate.Errors.ToList();
                errors.Add("A base não possui nó correspondente no relatório.");
                yield return new StrapNodePreviewItem(
                    candidate.NodeId,
                    candidate.ElementId,
                    null,
                    null,
                    errors);
            }
        }

        private void RaiseCounts()
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(DeselectedValidCount));
            OnPropertyChanged(nameof(CanConfirm));
            OnPropertyChanged(nameof(ConfirmText));
        }
    }
}
