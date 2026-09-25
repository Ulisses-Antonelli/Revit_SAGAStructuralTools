using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Mapping;
using SAGAStructuralTools.Core.Robot;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Threading;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class ImportRobotModelViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private readonly RobotParseResult _parseResult;
        private readonly RobotPlacementAnchor _anchor;
        private readonly Dispatcher _uiDispatcher;

        private GerdauCatalog _catalog;
        private string _catalogPath;
        private string _logDirectory;
        private bool _isBusy;
        private int _progress;

        public ImportRobotModelViewModel(Document doc, RobotParseResult parseResult, RobotPlacementAnchor anchor)
        {
            _doc = doc;
            _parseResult = parseResult;
            _anchor = anchor;
            _uiDispatcher = Dispatcher.CurrentDispatcher;

            Gaps.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasGaps));

            BrowseCatalogCommand = new RelayCommand(_ => BrowseCatalog());
            BrowseLogDirCommand = new RelayCommand(_ => BrowseLogDir());
            ImportCommand = new RelayCommand(_ => RunImport(), _ => CanImport());
            ApplyGapsCommand = new RelayCommand(_ => ApplyGaps(), _ => CanApplyGaps());

            _logDirectory = GerdauCatalogSettings.GetSavedLogDirectory()
                ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            var savedCatalog = GerdauCatalogSettings.GetSavedFolder();
            if (!string.IsNullOrWhiteSpace(savedCatalog) && Directory.Exists(savedCatalog))
                CatalogPath = savedCatalog;
        }

        public string CatalogPath
        {
            get => _catalogPath;
            set { if (Set(ref _catalogPath, value)) LoadCatalog(value); }
        }

        public string LogDirectory
        {
            get => _logDirectory;
            set { if (Set(ref _logDirectory, value)) SaveSettings(); }
        }

        public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
        public int Progress { get => _progress; set => Set(ref _progress, value); }
        public bool HasGaps => Gaps.Count > 0;

        public ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();
        public ObservableCollection<GapReviewItem> Gaps { get; } = new ObservableCollection<GapReviewItem>();

        public RelayCommand BrowseCatalogCommand { get; }
        public RelayCommand BrowseLogDirCommand { get; }
        public RelayCommand ImportCommand { get; }
        public RelayCommand ApplyGapsCommand { get; }

        private void BrowseCatalog()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Selecionar o diretório das famílias Gerdau (.rfa)" })
                if (dlg.ShowDialog() == DialogResult.OK) CatalogPath = dlg.SelectedPath;
        }

        private void BrowseLogDir()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Selecionar a pasta onde os logs serão salvos", SelectedPath = _logDirectory })
                if (dlg.ShowDialog() == DialogResult.OK) LogDirectory = dlg.SelectedPath;
        }

        private void LoadCatalog(string path)
        {
            SaveSettings();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) { _catalog = null; return; }
            _catalog = new GerdauCatalog();
            _catalog.Load(path);
            Log($"Catálogo carregado: {_catalog.BeamCount} viga(s), {_catalog.ColumnCount} pilar(es).");
        }

        private void SaveSettings() => GerdauCatalogSettings.Save(_catalogPath, _logDirectory);

        private bool CanImport() => !IsBusy && _catalog != null && _catalog.Count > 0;
        private bool CanApplyGaps() => !IsBusy && Gaps.Any(g => g.CanInclude && g.Include);

        private void RunImport()
        {
            IsBusy = true;
            LogEntries.Clear();
            Gaps.Clear();
            Progress = 0;

            try
            {
                Log($"{_parseResult.Members.Count} barra(s) com perfil no arquivo, {_parseResult.Warnings.Count} aviso(s) de leitura.");
                foreach (var w in _parseResult.Warnings) Log($"[LEITURA] {w}");

                var toPlace = new List<ResolvedRobotMember>();
                int total = Math.Max(_parseResult.Members.Count, 1);
                int done = 0;

                foreach (var member in _parseResult.Members)
                {
                    bool isColumn = RobotMemberClassifier.IsColumn(member);
                    var mapping = RobotProfileResolver.ResolveWithTolerance(_catalog, member.Profile, isColumn);
                    if (mapping != null)
                    {
                        toPlace.Add(new ResolvedRobotMember { Member = member, Mapping = mapping, IsColumn = isColumn });
                    }
                    else
                    {
                        var gap = RobotCatalogGapAnalyzer.Analyze(_catalog, member, isColumn);
                        Gaps.Add(new GapReviewItem(member, isColumn, gap));
                        if (gap == null)
                            Log($"[SEM SUGESTÃO] barra {member.BarId} ({member.Profile.RawDesignation}, {(isColumn ? "pilar" : "viga")}): perfil não reconhecido.");
                    }
                    done++;
                    Progress = (int)(100.0 * done / total);
                }

                Log($"{toPlace.Count} barra(s) prontas pra criar, {Gaps.Count} sem correspondência no catálogo.");
                var results = RobotMemberPlacementService.PlaceAll(_doc, toPlace, _anchor, msg => Log(msg));
                LogSummary(results);
                WriteLogFile();
            }
            catch (Exception ex)
            {
                Log($"[ERRO CRÍTICO] {ex.Message}");
                WriteLogFile();
            }
            finally { IsBusy = false; }
        }

        private void ApplyGaps()
        {
            IsBusy = true;
            try
            {
                var toApply = Gaps.Where(g => g.CanInclude && g.Include).ToList();

                foreach (var item in toApply)
                {
                    string row = UDobradoProfileFormat.BuildCatalogRow(item.Gap.Dimensions, item.WeightKgPerMeter);
                    var backup = RobotCatalogWriter.AppendRow(item.Gap.TargetCatalogTxtPath, row);
                    if (backup != null) Log($"[CATÁLOGO] backup criado: {backup}");
                    Log($"[CATÁLOGO] linha adicionada em '{Path.GetFileName(item.Gap.TargetCatalogTxtPath)}': {row}");
                }

                _catalog = new GerdauCatalog();
                _catalog.Load(_catalogPath);
                Log("Catálogo recarregado com as linhas novas.");

                var toPlace = new List<ResolvedRobotMember>();
                foreach (var item in toApply)
                {
                    var mapping = RobotProfileResolver.ResolveWithTolerance(_catalog, item.Member.Profile, item.IsColumn);
                    if (mapping != null)
                        toPlace.Add(new ResolvedRobotMember { Member = item.Member, Mapping = mapping, IsColumn = item.IsColumn });
                    else
                        Log($"[ERRO] barra {item.BarId}: mesmo depois de incluir no catálogo, não casou (confira o arquivo .txt).");
                }

                var results = RobotMemberPlacementService.PlaceAll(_doc, toPlace, _anchor, msg => Log(msg));
                LogSummary(results);

                var placedBarIds = new HashSet<int>(results.Where(r => r.Success).Select(r => r.BarId));
                foreach (var item in toApply.Where(i => placedBarIds.Contains(i.BarId)).ToList())
                    Gaps.Remove(item);

                WriteLogFile();
            }
            catch (Exception ex)
            {
                Log($"[ERRO CRÍTICO] {ex.Message}");
                WriteLogFile();
            }
            finally { IsBusy = false; }
        }

        private void LogSummary(List<RobotMemberResult> results)
        {
            int ok = results.Count(r => r.Success);
            Log($"{ok}/{results.Count} elemento(s) criado(s) com sucesso.");
            foreach (var r in results.Where(r => !r.Success))
                Log($"[FALHA] barra {r.BarId} ({r.RawDesignation}): {r.Message}");
        }

        private void WriteLogFile()
        {
            try
            {
                var dir = !string.IsNullOrWhiteSpace(_logDirectory) && Directory.Exists(_logDirectory)
                    ? _logDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var path = Path.Combine(dir, $"SAGA_Robot_Import_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
                File.WriteAllLines(path, LogEntries);
                Log($"[LOG] Arquivo salvo em: {path}");
            }
            catch (Exception ex)
            {
                Log($"[LOG] Falha ao salvar log: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _uiDispatcher.Invoke(() => LogEntries.Add(entry));
        }
    }
}
