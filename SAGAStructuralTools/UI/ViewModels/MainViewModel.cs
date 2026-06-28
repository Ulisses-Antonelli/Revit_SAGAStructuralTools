using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Conversion;
using SAGAStructuralTools.Core.Mapping;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Threading;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        // Arquivo de configuração salvo ao lado do .addin no diretório de addins do Revit
        private static readonly string SettingsFile = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGAStructuralTools.settings");

        private readonly UIApplication _uiApp;
        private readonly GerdauCatalog _catalog = new GerdauCatalog();
        private readonly ProfileMatcher _matcher;
        private readonly Dispatcher _uiDispatcher;

        private string _catalogPath;
        private string _logDirectory;
        private bool   _isConverting;
        private int    _progress;

        public MainViewModel(UIApplication uiApp)
        {
            _uiApp        = uiApp;
            _matcher      = new ProfileMatcher(_catalog);
            _uiDispatcher = Dispatcher.CurrentDispatcher;

            BrowseCatalogCommand = new RelayCommand(_ => BrowseCatalog());
            BrowseLogDirCommand  = new RelayCommand(_ => BrowseLogDir());
            ConvertCommand       = new RelayCommand(_ => ConvertSync(), _ => CanConvert());

            // Carrega caminhos salvos da última sessão
            var (savedCatalog, savedLogDir) = LoadSavedPaths();
            if (!string.IsNullOrWhiteSpace(savedCatalog) && Directory.Exists(savedCatalog))
                CatalogPath = savedCatalog;
            _logDirectory = !string.IsNullOrWhiteSpace(savedLogDir) && Directory.Exists(savedLogDir)
                ? savedLogDir
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        public string CatalogPath
        {
            get => _catalogPath;
            set { if (Set(ref _catalogPath, value)) LoadCatalog(value); }
        }

        public string LogDirectory
        {
            get => _logDirectory;
            set { if (Set(ref _logDirectory, value)) SavePaths(_catalogPath, value); }
        }

        public bool IsConverting
        {
            get => _isConverting;
            set => Set(ref _isConverting, value);
        }

        public int Progress
        {
            get => _progress;
            set => Set(ref _progress, value);
        }

        public ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();

        public RelayCommand BrowseCatalogCommand { get; }
        public RelayCommand BrowseLogDirCommand  { get; }
        public RelayCommand ConvertCommand       { get; }

        private void BrowseCatalog()
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description         = "Selecione o diretório das famílias (.rfa)",
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                    CatalogPath = dialog.SelectedPath;
            }
        }

        private void BrowseLogDir()
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description         = "Selecione a pasta onde os logs serão salvos",
                ShowNewFolderButton = true,
                SelectedPath        = _logDirectory
            })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                    LogDirectory = dialog.SelectedPath;
            }
        }

        private void LoadCatalog(string path)
        {
            _catalog.Load(path);
            SavePaths(path, _logDirectory);
            Log($"Catálogo carregado: {_catalog.BeamCount} vigas, {_catalog.ColumnCount} pilares.");
        }

        private static void SavePaths(string catalogPath, string logDir)
        {
            try { File.WriteAllLines(SettingsFile, new[] { catalogPath ?? "", logDir ?? "" }); }
            catch (Exception) { /* falhas de I/O são não-fatais */ }
        }

        private static (string catalog, string logDir) LoadSavedPaths()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return (null, null);
                var lines = File.ReadAllLines(SettingsFile);
                var catalog = lines.Length > 0 ? lines[0].Trim() : null;
                var logDir  = lines.Length > 1 ? lines[1].Trim() : null;
                return (catalog, logDir);
            }
            catch (Exception) { /* arquivo ausente ou ilegível */ }
            return (null, null);
        }

        private bool CanConvert() => !IsConverting && _catalog.Count > 0;

        private void ConvertSync()
        {
            IsConverting = true;
            LogEntries.Clear();
            Progress = 0;

            try
            {
                var hostDoc = _uiApp.ActiveUIDocument.Document;

                Log("Localizando elementos IFC...");

                // Detecta se o IFC está vinculado ou importado
                var linkInstance = new FilteredElementCollector(hostDoc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .FirstOrDefault();

                Document   sourceDoc;
                Transform  linkTransform;
                ElementId[] elementIds;

                if (linkInstance != null && linkInstance.GetLinkDocument() != null)
                {
                    sourceDoc     = linkInstance.GetLinkDocument();
                    linkTransform = linkInstance.GetTotalTransform();
                    elementIds    = CollectStructuralElements(sourceDoc);
                    Log($"IFC vinculado encontrado: '{sourceDoc.Title}'");
                    LogLinkedDiagnostics(sourceDoc, elementIds);
                }
                else
                {
                    sourceDoc     = hostDoc;
                    linkTransform = Transform.Identity;
                    elementIds    = CollectStructuralElements(hostDoc);
                    Log("Nenhum IFC vinculado — buscando no documento host.");
                }

                Log($"{elementIds.Length} elementos estruturais encontrados. Iniciando conversão...");

                if (elementIds.Length == 0)
                {
                    Log("Nenhum elemento encontrado. Verifique se o IFC está vinculado/importado e contém vigas ou pilares.");
                    return;
                }

                var converter     = new ElementConverter(hostDoc, sourceDoc, linkTransform);
                var progressReport = new SyncProgress(msg => Log(msg));

                var results = converter.ConvertAll(
                    elementIds,
                    (name, isCol) => isCol ? _matcher.MatchColumn(name) : _matcher.MatchBeam(name),
                    progressReport);

                var ok = 0; var fail = 0;
                foreach (var r in results)
                {
                    if (r.Status == Core.Models.ConversionStatus.Success) ok++;
                    else fail++;
                }

                Progress = 100;
                Log($"Concluído: {ok} convertidos, {fail} com erro ou não mapeados.");
                WriteLogFile();
            }
            catch (Exception ex)
            {
                Log($"[ERRO CRÍTICO] {ex.Message}");
                WriteLogFile();
            }
            finally
            {
                IsConverting = false;
            }
        }

        private static ElementId[] CollectStructuralElements(Document doc)
        {
            // OST_Columns ("Colunas") = categoria que o Revit usa para IfcColumn em IFCs vinculados
            // OST_StructuralColumns ("Pilares estruturais") = pilares nativos Revit
            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_GenericModel
            };

            var directShapeIds = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .WherePasses(new ElementMulticategoryFilter(categories))
                .Select(e => e.Id);

            var familyColumnIds = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .Select(e => e.Id);

            return directShapeIds.Concat(familyColumnIds).ToArray();
        }

        private void LogLinkedDiagnostics(Document linkedDoc, ElementId[] foundIds)
        {
            var totalDs = new FilteredElementCollector(linkedDoc)
                .OfClass(typeof(DirectShape)).GetElementCount();

            Log($"[DIAG] DirectShape total no IFC: {totalDs}");
            Log($"[DIAG] Elementos coletados (DirectShape + FamilyInstance): {foundIds.Length}");

            // Todas as categorias de DirectShape para identificar onde os pilares estão
            var allDsCategories = new FilteredElementCollector(linkedDoc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .GroupBy(e => e.Category?.Name ?? "Sem categoria")
                .Select(g => $"{g.Key}: {g.Count()}");

            Log($"[DIAG] DirectShape por categoria: {string.Join(" | ", allDsCategories)}");

            // FamilyInstance de coluna (IfcColumn mapeado para família Revit)
            var fiCols = new FilteredElementCollector(linkedDoc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .GetElementCount();
            Log($"[DIAG] FamilyInstance OST_StructuralColumns: {fiCols}");

            // Primeiros nomes coletados
            var samples = foundIds.Take(10)
                .Select(id => linkedDoc.GetElement(id))
                .Where(e => e != null)
                .Select(e => $"[{e.Category?.Name}] '{e.Name}'");

            foreach (var s in samples) Log($"[DIAG] {s}");
        }

        private void WriteLogFile()
        {
            try
            {
                var dir  = !string.IsNullOrWhiteSpace(_logDirectory) && Directory.Exists(_logDirectory)
                    ? _logDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                var path = Path.Combine(dir, $"SAGA_Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
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

    internal sealed class SyncProgress : IProgress<string>
    {
        private readonly Action<string> _callback;
        public SyncProgress(Action<string> callback) => _callback = callback;
        public void Report(string value) => _callback(value);
    }
}
