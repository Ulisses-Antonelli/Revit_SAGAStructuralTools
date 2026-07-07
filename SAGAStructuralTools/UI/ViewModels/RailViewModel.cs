using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Rail;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Threading;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class RailViewModel : ViewModelBase
    {
        private readonly Dispatcher          _dispatcher;
        private readonly ExternalEvent       _pickEvent;
        private readonly ExternalEvent       _createEvent;
        private readonly RailCreationHandler _createHandler;

        // Segmentos selecionados internamente
        private readonly List<(ElementId id, double lengthMm)> _segments = new List<(ElementId, double)>();

        public RailViewModel(ExternalEvent pickEvent, LinePickHandler pickHandler,
                             ExternalEvent createEvent, RailCreationHandler createHandler)
        {
            SagaLog.Write("RailViewModel — construtor início");
            _dispatcher    = Dispatcher.CurrentDispatcher;
            _pickEvent     = pickEvent;
            _createEvent   = createEvent;
            _createHandler = createHandler;

            pickHandler.LinesPicked    += OnLinesPicked;
            _createHandler.Completed   += OnCreationCompleted;

            SagaLog.Write("RailViewModel — criando comandos...");
            SelectPerimeterCommand    = new RelayCommand(_ => _pickEvent.Raise());
            BrowsePostCommand         = new RelayCommand(_ => BrowseFamily(ref _postFamilyPath,  ref _postFamilyType,  nameof(PostFamilyDisplay),  nameof(PostAvailableTypes),  PostAvailableTypes));
            BrowseHandrailCommand     = new RelayCommand(_ => BrowseFamily(ref _handrailFamilyPath, ref _handrailFamilyType, nameof(HandrailFamilyDisplay), nameof(HandrailAvailableTypes), HandrailAvailableTypes));
            BrowseFrameCommand        = new RelayCommand(_ => BrowseFamily(ref _frameFamilyPath, ref _frameFamilyType, nameof(FrameFamilyDisplay), nameof(FrameAvailableTypes), FrameAvailableTypes));
            BrowseRodapeCommand       = new RelayCommand(_ => BrowseFamily(ref _rodapeFamilyPath, ref _rodapeFamilyType, nameof(RodapeFamilyDisplay), nameof(RodapeAvailableTypes), RodapeAvailableTypes));
            AddBarCommand             = new RelayCommand(_ => AddBar());

            RemoveBarCommand          = new RelayCommand(o => RemoveBar(o as BarConfigVm));
            CalculatePreviewCommand   = new RelayCommand(_ => CalculatePreview(), _ => _segments.Count > 0);
            CreateCommand             = new RelayCommand(_ => CreateRail(), _ => IsCalculated && _definition?.IsValid == true);

            SagaLog.Write("RailViewModel — adicionando item inicial em HorizontalBars...");
            HorizontalBars.Add(new BarConfigVm { RowIndex = 1 });
            SagaLog.Write("RailViewModel — construtor OK");
        }

        // ── Seleção de perímetro ──────────────────────────────────────────

        public ObservableCollection<string> SegmentItems { get; } = new ObservableCollection<string>();

        private bool _hasSegments;
        public bool HasSegments { get => _hasSegments; set => Set(ref _hasSegments, value); }

        private void OnLinesPicked(List<(ElementId id, double lengthMm)> items)
        {
            _dispatcher.Invoke(() =>
            {
                _segments.Clear();
                _segments.AddRange(items);
                SegmentItems.Clear();
                for (int i = 0; i < items.Count; i++)
                    SegmentItems.Add($"{i + 1}  —  comprimento: {items[i].lengthMm:F0} mm");
                HasSegments = _segments.Count > 0;
                IsCalculated = false;
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            });
        }

        // ── Aba Entrada — distribuição ────────────────────────────────────

        private DistributionMode _distMode = DistributionMode.ByCount;
        public DistributionMode DistMode
        {
            get => _distMode;
            set
            {
                if (Set(ref _distMode, value))
                {
                    OnPropertyChanged(nameof(IsPostCountEnabled));
                    OnPropertyChanged(nameof(IsMaxPostSpanEnabled));
                    OnPropertyChanged(nameof(IsFixedAxisEnabled));
                }
            }
        }

        public bool IsPostCountEnabled   => DistMode == DistributionMode.ByCount;
        public bool IsMaxPostSpanEnabled => DistMode == DistributionMode.MaxSpan;
        public bool IsFixedAxisEnabled   => DistMode == DistributionMode.FixedAxis;

        private int    _postCount        = RailDefaults.PostCount;
        private double _maxPostSpan      = RailDefaults.MaxPostSpan;
        private double _fixedAxisSpacing = RailDefaults.FixedAxisSpacing;

        public int    PostCount        { get => _postCount;        set => Set(ref _postCount,        value); }
        public double MaxPostSpan      { get => _maxPostSpan;      set => Set(ref _maxPostSpan,      value); }
        public double FixedAxisSpacing { get => _fixedAxisSpacing; set => Set(ref _fixedAxisSpacing, value); }

        // ── Montante ──────────────────────────────────────────────────────

        private string _postFamilyPath;
        private string _postFamilyType;
        private double _postRotation   = RailDefaults.PostRotation;
        private double _postTopOffset  = RailDefaults.PostTopOffset;
        private double _postBaseOffset = RailDefaults.PostBaseOffset;
        private double _postAxisOffset = RailDefaults.PostAxisOffset;

        public string PostFamilyDisplay  => FamilyDisplay(_postFamilyPath, _postFamilyType);
        public string PostFamilyType     { get => _postFamilyType;  set { if (Set(ref _postFamilyType,  value)) OnPropertyChanged(nameof(PostFamilyDisplay)); } }
        public double PostRotation       { get => _postRotation;    set => Set(ref _postRotation,        value); }
        public double PostTopOffset      { get => _postTopOffset;   set => Set(ref _postTopOffset,       value); }
        public double PostBaseOffset     { get => _postBaseOffset;  set => Set(ref _postBaseOffset,      value); }
        public double PostAxisOffset     { get => _postAxisOffset;  set => Set(ref _postAxisOffset,      value); }
        public ObservableCollection<string> PostAvailableTypes { get; } = new ObservableCollection<string>();

        // ── Corrimão ──────────────────────────────────────────────────────

        private string _handrailFamilyPath;
        private string _handrailFamilyType;
        private double _handrailRotation   = RailDefaults.HandrailRotation;
        private double _handrailAxisOffset = RailDefaults.HandrailAxisOffset;
        private HandrailJustification _justification = HandrailJustification.Center;
        private double _handrailHeight     = RailDefaults.HandrailHeight;

        public string HandrailFamilyDisplay => FamilyDisplay(_handrailFamilyPath, _handrailFamilyType);
        public string HandrailFamilyType    { get => _handrailFamilyType;  set { if (Set(ref _handrailFamilyType, value)) OnPropertyChanged(nameof(HandrailFamilyDisplay)); } }
        public double HandrailRotation      { get => _handrailRotation;    set => Set(ref _handrailRotation,    value); }
        public double HandrailAxisOffset    { get => _handrailAxisOffset;  set => Set(ref _handrailAxisOffset,  value); }
        public HandrailJustification Justification { get => _justification; set => Set(ref _justification, value); }
        public double HandrailHeight        { get => _handrailHeight;      set => Set(ref _handrailHeight,      value); }
        public ObservableCollection<string> HandrailAvailableTypes { get; } = new ObservableCollection<string>();

        // ── Aba Fechamento ────────────────────────────────────────────────

        private InfillMode _infillMode = InfillMode.HorizontalBars;
        public InfillMode InfillMode
        {
            get => _infillMode;
            set
            {
                if (Set(ref _infillMode, value))
                {
                    OnPropertyChanged(nameof(IsHorizontalBarsMode));
                    OnPropertyChanged(nameof(IsFramePanelMode));
                }
            }
        }
        public bool IsHorizontalBarsMode => InfillMode == InfillMode.HorizontalBars;
        public bool IsFramePanelMode     => InfillMode == InfillMode.FramePanel;

        public ObservableCollection<BarConfigVm> HorizontalBars { get; } = new ObservableCollection<BarConfigVm>();

        private bool _sameProfileAll   = true;
        private bool _equidistantBars  = true;
        public bool SameProfileAll    { get => _sameProfileAll;  set => Set(ref _sameProfileAll,  value); }
        public bool EquidistantBars
        {
            get => _equidistantBars;
            set { if (Set(ref _equidistantBars, value)) OnPropertyChanged(nameof(IsDistanceColumnEnabled)); }
        }
        public bool IsDistanceColumnEnabled => !EquidistantBars;

        // Rodapé
        private string _rodapeFamilyPath;
        private string _rodapeFamilyType;
        private BarAlignment _rodapeAlignment = BarAlignment.InternalFace;
        private double _rodapeOffset;

        public string RodapeFamilyDisplay => FamilyDisplay(_rodapeFamilyPath, _rodapeFamilyType);
        public string RodapeFamilyType    { get => _rodapeFamilyType;  set { if (Set(ref _rodapeFamilyType, value)) OnPropertyChanged(nameof(RodapeFamilyDisplay)); } }
        public ObservableCollection<string> RodapeAvailableTypes { get; } = new ObservableCollection<string>();
        public BarAlignment RodapeAlignment { get => _rodapeAlignment; set => Set(ref _rodapeAlignment, value); }
        public double RodapeOffset          { get => _rodapeOffset;    set => Set(ref _rodapeOffset,    value); }

        // Fechamento em Quadro
        private FrameType _frameType = FrameType.AngleIron;
        public FrameType FrameType
        {
            get => _frameType;
            set
            {
                if (Set(ref _frameType, value))
                {
                    OnPropertyChanged(nameof(IsAngleIronType));
                    OnPropertyChanged(nameof(IsHorizontalOnlyType));
                }
            }
        }
        public bool IsAngleIronType      => FrameType == FrameType.AngleIron;
        public bool IsHorizontalOnlyType => FrameType == FrameType.HorizontalOnly;

        private string _frameFamilyPath;
        private string _frameFamilyType;
        private FrameAlignment _frameAlignment = FrameAlignment.ExternalFace;
        private double _frameOffset  = RailDefaults.FrameOffset;
        private double _frameHeight  = RailDefaults.FrameHeight;

        public string FrameFamilyDisplay  => FamilyDisplay(_frameFamilyPath, _frameFamilyType);
        public string FrameFamilyType     { get => _frameFamilyType;  set { if (Set(ref _frameFamilyType, value)) OnPropertyChanged(nameof(FrameFamilyDisplay)); } }
        public FrameAlignment FrameAlignment { get => _frameAlignment; set => Set(ref _frameAlignment, value); }
        public double FrameOffset         { get => _frameOffset;      set => Set(ref _frameOffset,      value); }
        public double FrameHeight         { get => _frameHeight;      set => Set(ref _frameHeight,      value); }
        public ObservableCollection<string> FrameAvailableTypes { get; } = new ObservableCollection<string>();

        // ── Aba Terminais ──────────────────────────────────────────────────

        private TerminalType _terminalType = TerminalType.Sharp;
        public TerminalType TerminalType
        {
            get => _terminalType;
            set { if (Set(ref _terminalType, value)) OnPropertyChanged(nameof(IsTerminalRadiusEnabled)); }
        }
        public bool IsTerminalRadiusEnabled => TerminalType == TerminalType.Rounded;

        private double _terminalRadius;
        public double TerminalRadius { get => _terminalRadius; set => Set(ref _terminalRadius, value); }

        // ── Preview ───────────────────────────────────────────────────────

        private RailDefinition _definition;
        private bool _isCalculated;
        public bool IsCalculated { get => _isCalculated; set => Set(ref _isCalculated, value); }
        public ObservableCollection<string> Warnings { get; } = new ObservableCollection<string>();

        // Valores expostos para o preview
        public double PreviewTotalLength   => _definition?.TotalLength   ?? 0;
        public int    PreviewTotalPosts    => _definition?.TotalPosts    ?? 0;
        public double PreviewSpacing       => _definition?.PreviewSpacing ?? 0;

        private void CalculatePreview()
        {
            try
            {
                var lengths = _segments.Select(s => s.lengthMm).ToList();
                _definition  = RailCalculator.Calculate(lengths, BuildConfig());
                IsCalculated = true;

                OnPropertyChanged(nameof(PreviewTotalLength));
                OnPropertyChanged(nameof(PreviewTotalPosts));
                OnPropertyChanged(nameof(PreviewSpacing));

                System.Windows.Input.CommandManager.InvalidateRequerySuggested();

                Warnings.Clear();
                foreach (var w in _definition.Warnings) Warnings.Add(w);
            }
            catch (Exception ex)
            {
                Warnings.Clear();
                Warnings.Add($"Erro no cálculo: {ex.Message}");
            }
        }

        // ── Criação ───────────────────────────────────────────────────────

        private void CreateRail()
        {
            _createHandler.Definition = _definition;
            _createHandler.Config     = BuildConfig();
            _createHandler.SegmentIds = _segments.Select(s => s.id).ToList();
            _createEvent.Raise();
        }

        private void OnCreationCompleted(string error)
        {
            _dispatcher.Invoke(() =>
            {
                if (error == null)
                {
                    Warnings.Clear();
                    Warnings.Add("Guarda-corpo criado com sucesso.");
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Erro ao criar guarda-corpo:\n\n{error}",
                        "SAGA Structural Tools",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private RailConfig BuildConfig() => new RailConfig
        {
            DistMode           = DistMode,
            PostCount          = PostCount,
            MaxPostSpan        = MaxPostSpan,
            FixedAxisSpacing   = FixedAxisSpacing,

            PostFamilyPath     = _postFamilyPath,
            PostFamilyType     = _postFamilyType,
            PostRotation       = PostRotation,
            PostTopOffset      = PostTopOffset,
            PostBaseOffset     = PostBaseOffset,
            PostAxisOffset     = PostAxisOffset,

            HandrailFamilyPath = _handrailFamilyPath,
            HandrailFamilyType = _handrailFamilyType,
            HandrailRotation   = HandrailRotation,
            HandrailAxisOffset = HandrailAxisOffset,
            Justification      = Justification,
            HandrailHeight     = HandrailHeight,

            InfillMode         = InfillMode,
            HorizontalBars     = HorizontalBars.Select(b => b.ToModel()).ToList(),
            SameProfileAll     = SameProfileAll,
            EquidistantBars    = EquidistantBars,
            Rodape             = new BarConfig { FamilyPath = _rodapeFamilyPath, FamilyType = _rodapeFamilyType, Alignment = RodapeAlignment, Distance = RodapeOffset },

            FrameType          = FrameType,
            FrameFamilyPath    = _frameFamilyPath,
            FrameFamilyType    = _frameFamilyType,
            FrameAlignment     = FrameAlignment,
            FrameOffset        = FrameOffset,
            FrameHeight        = FrameHeight,

            TerminalType       = TerminalType,
            TerminalRadius     = TerminalRadius
        };

        private void BrowseFamily(ref string pathField, ref string typeField,
                                   string displayProp, string typesProp,
                                   ObservableCollection<string> typesCollection)
        {
            using (var dlg = new OpenFileDialog
            {
                Title  = "Selecionar família (.rfa)",
                Filter = "Revit Family (*.rfa)|*.rfa"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                pathField = dlg.FileName;
                LoadTypesFromCatalog(dlg.FileName, typeField, typesCollection, out typeField);
                OnPropertyChanged(displayProp);
                OnPropertyChanged(typesProp);
            }
        }

        private void LoadTypesFromCatalog(string rfaPath, string currentType,
                                           ObservableCollection<string> target, out string selectedType)
        {
            target.Clear();
            selectedType = null;

            var catalogPath = Path.ChangeExtension(rfaPath, ".txt");
            if (!File.Exists(catalogPath)) return;

            try
            {
                var lines = File.ReadAllLines(catalogPath, Encoding.Default);
                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("##")) continue;
                    var name = line.Split(',')[0].Trim().Replace("\"\"", "\"");
                    if (!string.IsNullOrWhiteSpace(name) && !name.Contains("##"))
                        target.Add(name);
                }
                if (target.Count > 0) selectedType = target[0];
            }
            catch { }
        }

        private static string FamilyDisplay(string path, string type)
        {
            if (string.IsNullOrWhiteSpace(path)) return "Selecione uma família (.rfa)";
            var name = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(type) ? name : $"{name}  ·  {type}";
        }

        private void AddBar()
        {
            HorizontalBars.Add(new BarConfigVm { RowIndex = HorizontalBars.Count + 1 });
            RenumberBars();
        }

        private void RemoveBar(BarConfigVm bar)
        {
            if (bar == null || HorizontalBars.Count <= 1) return;
            HorizontalBars.Remove(bar);
            RenumberBars();
        }

        private void RenumberBars()
        {
            for (int i = 0; i < HorizontalBars.Count; i++)
                HorizontalBars[i].RowIndex = i + 1;
        }

        // ── Commands ─────────────────────────────────────────────────────

        public RelayCommand SelectPerimeterCommand  { get; }
        public RelayCommand BrowsePostCommand       { get; }
        public RelayCommand BrowseHandrailCommand   { get; }
        public RelayCommand BrowseFrameCommand      { get; }
        public RelayCommand BrowseRodapeCommand     { get; }
        public RelayCommand AddBarCommand           { get; }
        public RelayCommand RemoveBarCommand        { get; }
        public RelayCommand CalculatePreviewCommand { get; }
        public RelayCommand CreateCommand           { get; }
    }
}
