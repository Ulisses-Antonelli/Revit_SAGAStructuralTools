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
        private readonly Func<StrapTargetProfile, IEnumerable<StrapNodePreviewItem>> _rowFactory;
        private readonly Action<string> _log;
        private string _searchText = string.Empty;
        private StrapPreviewFilter _filter;
        private StrapNodePreviewItem _selectedRow;
        private StrapTargetProfile _selectedTargetProfile;

        internal ImportStrapReactionsViewModel(string filePath, StrapPartialImportResult import,
            IEnumerable<StrapTargetProfile> profiles, StrapCandidateCatalog catalog,
            Action<string> log = null)
            : this(filePath, string.Join(", ", import.ParseResult.ForceUnits),
                  string.Join(", ", import.ParseResult.MomentUnits),
                  import.GlobalDiagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).Select(x => x.Message),
                  profiles, profile => profile == null ? BuildAwaitingRows(import) :
                      BuildRows(import, catalog.Get(profile), profile, log), log) { }

        internal ImportStrapReactionsViewModel(string filePath, string forceUnit,
            string momentUnit, IEnumerable<string> globalErrors,
            IEnumerable<StrapNodePreviewItem> rows)
            : this(filePath, forceUnit, momentUnit, globalErrors,
                  Array.Empty<StrapTargetProfile>(), _ => rows, null) { }

        internal ImportStrapReactionsViewModel(string filePath, string forceUnit,
            string momentUnit, IEnumerable<string> globalErrors,
            IEnumerable<StrapTargetProfile> profiles,
            Func<StrapTargetProfile, IEnumerable<StrapNodePreviewItem>> rowFactory,
            Action<string> log = null)
        {
            FilePath = filePath;
            ForceUnit = forceUnit;
            MomentUnit = momentUnit;
            GlobalErrors = (globalErrors ?? Array.Empty<string>()).ToArray();
            TargetProfiles = (profiles ?? Array.Empty<StrapTargetProfile>()).ToArray();
            _rowFactory = rowFactory ?? throw new ArgumentNullException(nameof(rowFactory));
            _log = log;
            SelectPendingCommand = new RelayCommand(_ => SelectPending());
            DeselectAllCommand = new RelayCommand(_ => DeselectAll());
            ShowAllCommand = new RelayCommand(_ => Filter = StrapPreviewFilter.All);
            ShowValidCommand = new RelayCommand(_ => Filter = StrapPreviewFilter.Valid);
            ShowErrorsCommand = new RelayCommand(_ => Filter = StrapPreviewFilter.Error);
            ReplaceRows(_rowFactory(null));
        }

        public string FilePath { get; }
        public string ForceUnit { get; }
        public string MomentUnit { get; }
        public string UnitNotice => "Os valores serão armazenados convencionalmente como Number: forças em tf e momentos em tf·m, sem conversão pelo Revit.";
        public IReadOnlyCollection<string> GlobalErrors { get; }
        public bool HasGlobalErrors => GlobalErrors.Count != 0;
        public IReadOnlyCollection<StrapTargetProfile> TargetProfiles { get; }
        public ObservableCollection<StrapNodePreviewItem> Rows { get; private set; }
        public ICollectionView RowsView { get; private set; }
        public ICommand SelectPendingCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand ShowAllCommand { get; }
        public ICommand ShowValidCommand { get; }
        public ICommand ShowErrorsCommand { get; }

        public StrapTargetProfile SelectedTargetProfile
        {
            get => _selectedTargetProfile;
            set
            {
                if (ReferenceEquals(_selectedTargetProfile, value)) return;
                var rotations = Rows.Where(row => row.Rotate90)
                    .GroupBy(row => row.NodeId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => true, StringComparer.Ordinal);
                _selectedTargetProfile = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedTargetProfile));
                OnPropertyChanged(nameof(CategoryGuidance));
                ReplaceRows(_rowFactory(value));
                foreach (var row in Rows)
                {
                    if (rotations.ContainsKey(row.NodeId)) row.Rotate90 = true;
                    row.IsSelected = false;
                }
                _selectedRow = null;
                OnPropertyChanged(nameof(SelectedRow));
                _log?.Invoke(value == null
                    ? "Categoria de destino removida. Mapeamento invalidado."
                    : $"Categoria de destino selecionada: perfil={value.Id}, categoria={value.BuiltInCategory}, candidatos/linhas={Rows.Count}.");
                RaiseCounts();
            }
        }

        public bool HasSelectedTargetProfile => SelectedTargetProfile != null;
        public string CategoryGuidance => HasSelectedTargetProfile ? string.Empty : "Selecione a categoria de destino";
        public string SearchText { get => _searchText; set { if (Set(ref _searchText, value ?? string.Empty)) { RowsView.Refresh(); RaiseCounts(); } } }
        public StrapPreviewFilter Filter { get => _filter; set { if (Set(ref _filter, value)) { RowsView.Refresh(); RaiseCounts(); } } }
        public StrapNodePreviewItem SelectedRow { get => _selectedRow; set => Set(ref _selectedRow, value); }
        public int FoundCount => Rows.Count;
        public int VisibleCount => RowsView.Cast<object>().Count();
        public int ValidCount => Rows.Count(x => x.IsValid);
        public int PendingCount => Rows.Count(x => x.IsPending);
        public int AlreadyUpdatedCount => Rows.Count(x => x.IsAlreadyUpdated);
        public int SelectedForUpdateCount => Rows.Count(x => x.IsPending && x.IsSelected);
        public int InvalidCount => Rows.Count(x => x.IsInvalid);
        public int DeselectedValidCount => Rows.Count(x => x.IsValid && !x.IsSelected);
        public bool CanConfirm => HasSelectedTargetProfile && !HasGlobalErrors && SelectedForUpdateCount > 0;
        public string ConfirmText => $"Atualizar {SelectedForUpdateCount} elemento(s)";

        internal ReactionWritePlan CreatePlan()
        {
            if (!CanConfirm) throw new InvalidOperationException("Nenhuma alteração pendente foi selecionada.");
            return new ReactionWritePlan(Rows.Where(x => x.IsPending && x.IsSelected).Select(x => x.ToPlanItem()));
        }

        private void ReplaceRows(IEnumerable<StrapNodePreviewItem> rows)
        {
            Rows = new ObservableCollection<StrapNodePreviewItem>(rows ?? Array.Empty<StrapNodePreviewItem>());
            foreach (var row in Rows)
            {
                row.SelectionChanged += (s, e) => RaiseCounts();
                row.StateChanged += (s, e) => { RowsView.Refresh(); RaiseCounts(); };
            }
            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = IsVisible;
            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(RowsView));
        }

        private bool IsVisible(object value)
        {
            var row = (StrapNodePreviewItem)value;
            bool search = string.IsNullOrWhiteSpace(SearchText) ||
                (row.NodeId ?? string.Empty).IndexOf(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            bool filter = Filter == StrapPreviewFilter.All ||
                (Filter == StrapPreviewFilter.Valid && row.IsValid) ||
                (Filter == StrapPreviewFilter.Error && row.IsInvalid);
            return search && filter;
        }

        private void SelectPending() { foreach (var row in Rows.Where(x => x.IsPending)) row.IsSelected = true; }
        private void DeselectAll() { foreach (var row in Rows.Where(x => x.IsValid)) row.IsSelected = false; }

        private static IEnumerable<StrapNodePreviewItem> BuildAwaitingRows(StrapPartialImportResult import)
        {
            foreach (var node in import.Nodes)
            {
                string[] errors = node.Diagnostics
                    .Where(item => item.Severity == DiagnosticSeverity.Error)
                    .Select(item => item.Message).ToArray();
                yield return new StrapNodePreviewItem(node.NodeId, null,
                    node.Node.Declaration?.Trace.LineNumber, node.Reaction, errors,
                    isAwaitingCategory: node.Reaction != null && errors.Length == 0);
            }
        }

        private static IEnumerable<StrapNodePreviewItem> BuildRows(StrapPartialImportResult import,
            IEnumerable<StrapTargetCandidate> source, StrapTargetProfile profile, Action<string> log)
        {
            var candidates = (source ?? Array.Empty<StrapTargetCandidate>()).ToArray();
            log?.Invoke($"Mapeamento STRAP: perfil={profile.Id}, categoria={profile.BuiltInCategory}, candidatos={candidates.Length}.");
            var used = new HashSet<long>();
            foreach (var node in import.Nodes)
            {
                StrapTargetMatch targetMatch = StrapTargetMapping.Match(profile, node.NodeId, candidates);
                var matches = targetMatch.Candidates.ToArray();
                var errors = node.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error || x.Code == DiagnosticCodes.DuplicateNodeIdentical).Select(x => x.Message).ToList();
                if (node.Reaction == null && errors.Count == 0) errors.Add("Linha bloqueada por um erro global do documento.");
                if (matches.Length == 0)
                {
                    log?.Invoke($"STRAP sem correspondência: perfil={profile.Id}, NodeId={node.NodeId}.");
                    yield return new StrapNodePreviewItem(node.NodeId, null, node.Node.Declaration?.Trace.LineNumber,
                        node.Reaction, errors.Concat(new[] { targetMatch.Error }), targetProfile: profile);
                }
                else if (matches.Length > 1)
                {
                    log?.Invoke($"Duplicidade STRAP: perfil={profile.Id}, NodeId={node.NodeId}, ElementIds={string.Join(",", matches.Select(x => x.ElementId))}.");
                    foreach (var match in matches)
                    {
                        used.Add(match.ElementId);
                        yield return new StrapNodePreviewItem(node.NodeId, match.ElementId,
                            node.Node.Declaration?.Trace.LineNumber, node.Reaction,
                            errors.Concat(match.Errors).Concat(new[] { targetMatch.Error }), match.CurrentValues, profile);
                    }
                }
                else
                {
                    var match = matches[0]; used.Add(match.ElementId);
                    log?.Invoke($"Mapeamento STRAP: perfil={profile.Id}, NodeId={node.NodeId}, ElementId={match.ElementId}.");
                    yield return new StrapNodePreviewItem(node.NodeId, match.ElementId,
                        node.Node.Declaration?.Trace.LineNumber, node.Reaction,
                        errors.Concat(match.Errors), match.CurrentValues, profile);
                }
            }
            foreach (var candidate in candidates.Where(x => !used.Contains(x.ElementId)))
                yield return new StrapNodePreviewItem(candidate.NodeId, candidate.ElementId, null, null,
                    candidate.Errors.Concat(new[] { $"O {profile.EntitySingular} não possui nó correspondente no relatório." }),
                    candidate.CurrentValues, profile);
        }

        private void RaiseCounts()
        {
            foreach (string name in new[] { nameof(FoundCount), nameof(VisibleCount), nameof(ValidCount), nameof(PendingCount), nameof(AlreadyUpdatedCount), nameof(SelectedForUpdateCount), nameof(InvalidCount), nameof(DeselectedValidCount), nameof(CanConfirm), nameof(ConfirmText) }) OnPropertyChanged(name);
        }
    }
}
