using SAGAStructuralTools.Revit.Strap;
using SAGAStructuralTools.Strap.Application;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;

namespace SAGAStructuralTools.UI.ViewModels.Strap
{
    internal sealed class ImportStrapReactionsViewModel : ViewModelBase
    {
        private string _searchText = string.Empty;
        private StrapPreviewFilter _filter;
        private StrapNodePreviewItem _selectedRow;

        internal ImportStrapReactionsViewModel(string filePath, StrapPartialImportResult import,
            IEnumerable<StrapConnectionCandidate> candidates)
            : this(filePath, string.Join(", ", import.ParseResult.ForceUnits),
                  string.Join(", ", import.ParseResult.MomentUnits),
                  import.GlobalDiagnostics.Where(x => x.Severity == DiagnosticSeverity.Error)
                      .Select(x => x.Message), BuildRows(import, candidates)) { }

        internal ImportStrapReactionsViewModel(string filePath, string forceUnit,
            string momentUnit, IEnumerable<string> globalErrors,
            IEnumerable<StrapNodePreviewItem> rows)
        {
            FilePath = filePath;
            ForceUnit = forceUnit;
            MomentUnit = momentUnit;
            GlobalErrors = (globalErrors ?? Array.Empty<string>()).ToArray();
            Rows = new ObservableCollection<StrapNodePreviewItem>(rows ?? Array.Empty<StrapNodePreviewItem>());
            foreach (var row in Rows)
            {
                row.SelectionChanged += (s, e) => RaiseCounts();
                row.StateChanged += (s, e) => { RowsView.Refresh(); RaiseCounts(); };
            }
            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = IsVisible;
            SelectPendingCommand = new RelayCommand(_ => SelectPending());
            DeselectAllCommand = new RelayCommand(_ => DeselectAll());
            ShowAllCommand = new RelayCommand(_ => Filter = StrapPreviewFilter.All);
            ShowValidCommand = new RelayCommand(_ => Filter = StrapPreviewFilter.Valid);
            ShowErrorsCommand = new RelayCommand(_ => Filter = StrapPreviewFilter.Error);
        }

        public string FilePath { get; }
        public string ForceUnit { get; }
        public string MomentUnit { get; }
        public string UnitNotice => "Os valores serão armazenados convencionalmente como Number: forças em tf e momentos em tf·m, sem conversão pelo Revit.";
        public IReadOnlyCollection<string> GlobalErrors { get; }
        public bool HasGlobalErrors => GlobalErrors.Count != 0;
        public ObservableCollection<StrapNodePreviewItem> Rows { get; }
        public ICollectionView RowsView { get; }
        public ICommand SelectPendingCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand ShowAllCommand { get; }
        public ICommand ShowValidCommand { get; }
        public ICommand ShowErrorsCommand { get; }

        public string SearchText { get => _searchText; set { if (Set(ref _searchText, value ?? string.Empty)) { RowsView.Refresh(); RaiseCounts(); } } }
        public StrapPreviewFilter Filter { get => _filter; set { if (Set(ref _filter, value)) { RowsView.Refresh(); RaiseCounts(); } } }
        public StrapNodePreviewItem SelectedRow { get => _selectedRow; set => Set(ref _selectedRow, value); }
        public int FoundCount => Rows.Count;
        public int VisibleCount => RowsView.Cast<object>().Count();
        public int ValidCount => Rows.Count(x => x.IsValid);
        public int PendingCount => Rows.Count(x => x.IsPending);
        public int AlreadyUpdatedCount => Rows.Count(x => x.IsAlreadyUpdated);
        public int SelectedForUpdateCount => Rows.Count(x => x.IsPending && x.IsSelected);
        public int InvalidCount => Rows.Count(x => !x.IsValid);
        public int DeselectedValidCount => Rows.Count(x => x.IsValid && !x.IsSelected);
        public bool CanConfirm => !HasGlobalErrors && SelectedForUpdateCount > 0;
        public string ConfirmText => $"Atualizar {SelectedForUpdateCount} elemento(s)";

        internal ReactionWritePlan CreatePlan()
        {
            if (!CanConfirm) throw new InvalidOperationException("Nenhuma alteração pendente foi selecionada.");
            return new ReactionWritePlan(Rows.Where(x => x.IsPending && x.IsSelected).Select(x => x.ToPlanItem()));
        }

        private bool IsVisible(object value)
        {
            var row = (StrapNodePreviewItem)value;
            bool search = string.IsNullOrWhiteSpace(SearchText) ||
                (row.NodeId ?? string.Empty).IndexOf(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            bool filter = Filter == StrapPreviewFilter.All ||
                (Filter == StrapPreviewFilter.Valid && row.IsValid) ||
                (Filter == StrapPreviewFilter.Error && !row.IsValid);
            return search && filter;
        }

        private void SelectPending() { foreach (var row in Rows.Where(x => x.IsPending)) row.IsSelected = true; }
        private void DeselectAll() { foreach (var row in Rows.Where(x => x.IsValid)) row.IsSelected = false; }

        private static IEnumerable<StrapNodePreviewItem> BuildRows(StrapPartialImportResult import, IEnumerable<StrapConnectionCandidate> source)
        {
            var candidates = (source ?? Array.Empty<StrapConnectionCandidate>()).ToArray();
            var used = new HashSet<long>();
            foreach (var node in import.Nodes)
            {
                var matches = candidates.Where(x => string.Equals(x.NodeId, node.NodeId, StringComparison.Ordinal)).ToArray();
                var errors = node.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error || x.Code == DiagnosticCodes.DuplicateNodeIdentical).Select(x => x.Message).ToList();
                if (node.Reaction == null && errors.Count == 0) errors.Add("Linha bloqueada por um erro global do documento.");
                if (matches.Length == 0)
                    yield return new StrapNodePreviewItem(node.NodeId, null, node.Node.Declaration?.Trace.LineNumber, node.Reaction, errors);
                else if (matches.Length > 1)
                    foreach (var match in matches) { used.Add(match.ElementId); yield return new StrapNodePreviewItem(node.NodeId, match.ElementId, node.Node.Declaration?.Trace.LineNumber, node.Reaction, errors.Concat(match.Errors).Concat(new[] { "Mais de uma conexão possui o mesmo SGA_NO_PILAR." }), match.CurrentValues); }
                else { var match = matches[0]; used.Add(match.ElementId); yield return new StrapNodePreviewItem(node.NodeId, match.ElementId, node.Node.Declaration?.Trace.LineNumber, node.Reaction, errors.Concat(match.Errors), match.CurrentValues); }
            }
            foreach (var candidate in candidates.Where(x => !used.Contains(x.ElementId)))
                yield return new StrapNodePreviewItem(candidate.NodeId, candidate.ElementId, null, null, candidate.Errors.Concat(new[] { "A base não possui nó correspondente no relatório." }), candidate.CurrentValues);
        }

        private void RaiseCounts()
        {
            foreach (string name in new[] { nameof(VisibleCount), nameof(ValidCount), nameof(PendingCount), nameof(AlreadyUpdatedCount), nameof(SelectedForUpdateCount), nameof(InvalidCount), nameof(DeselectedValidCount), nameof(CanConfirm), nameof(ConfirmText) }) OnPropertyChanged(name);
        }
    }
}
