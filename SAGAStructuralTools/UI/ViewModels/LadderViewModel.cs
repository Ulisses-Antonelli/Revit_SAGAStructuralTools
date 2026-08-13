using Autodesk.Revit.UI;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Ladder;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Threading;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class LadderViewModel : ViewModelBase
    {
        private readonly Dispatcher            _dispatcher;
        private readonly ExternalEvent         _pickEvent;
        private readonly ExternalEvent         _createEvent;
        private readonly LadderCreationHandler _createHandler;
        private readonly LadderEditContext     _editContext;
        private bool                           _isSubmitting;

        private LadderPlacement  _placement;
        private LadderDefinition _definition;

        public LadderViewModel(ExternalEvent pickEvent, LadderPickHandler pickHandler,
                               ExternalEvent createEvent, LadderCreationHandler createHandler,
                               LadderEditContext editContext = null)
        {
            SagaLog.Write("LadderViewModel — construtor início");
            _dispatcher    = Dispatcher.CurrentDispatcher;
            _pickEvent     = pickEvent;
            _createEvent   = createEvent;
            _createHandler = createHandler;
            _editContext   = editContext;

            if (pickHandler != null)
            {
                pickHandler.PlacementPicked += OnPlacementPicked;
                pickHandler.PickFailed      += OnPickFailed;
            }
            _createHandler.Completed += OnCreationCompleted;

            PickReferenceCommand    = new RelayCommand(_ => _pickEvent.Raise(), _ => !_isSubmitting);
            CalculatePreviewCommand = new RelayCommand(_ => CalculatePreview(), _ => HasPlacement);
            CreateCommand           = new RelayCommand(_ => CreateLadder(), _ => HasPlacement && !_isSubmitting);

            BrowseStringerCommand = new RelayCommand(_ => BrowseFamily(ref _stringerFamilyPath, ref _stringerFamilyType, nameof(StringerFamilyDisplay), nameof(StringerAvailableTypes), nameof(StringerFamilyType), StringerAvailableTypes));
            BrowseRungCommand     = new RelayCommand(_ => BrowseFamily(ref _rungFamilyPath, ref _rungFamilyType, nameof(RungFamilyDisplay), nameof(RungAvailableTypes), nameof(RungFamilyType), RungAvailableTypes));
            BrowseSupportCommand  = new RelayCommand(_ => BrowseFamily(ref _supportFamilyPath, ref _supportFamilyType, nameof(SupportFamilyDisplay), nameof(SupportAvailableTypes), nameof(SupportFamilyType), SupportAvailableTypes));
            BrowseRingCommand     = new RelayCommand(_ => BrowseFamily(ref _ringFamilyPath, ref _ringFamilyType, nameof(RingFamilyDisplay), nameof(RingAvailableTypes), nameof(RingFamilyType), RingAvailableTypes));
            BrowseStrapCommand    = new RelayCommand(_ => BrowseFamily(ref _strapFamilyPath, ref _strapFamilyType, nameof(StrapFamilyDisplay), nameof(StrapAvailableTypes), nameof(StrapFamilyType), StrapAvailableTypes));
            BrowseLifelineCommand = new RelayCommand(_ => BrowseFamily(ref _lifelineFamilyPath, ref _lifelineFamilyType, nameof(LifelineFamilyDisplay), nameof(LifelineAvailableTypes), nameof(LifelineFamilyType), LifelineAvailableTypes));

            ClearSupportCommand  = new RelayCommand(_ => ClearFamily(ref _supportFamilyPath, ref _supportFamilyType, SupportAvailableTypes, nameof(SupportFamilyDisplay), nameof(SupportAvailableTypes), nameof(SupportFamilyType)),
                                                    _ => HasFamilySelection(_supportFamilyPath, _supportFamilyType, SupportAvailableTypes));
            ClearRingCommand     = new RelayCommand(_ => ClearFamily(ref _ringFamilyPath, ref _ringFamilyType, RingAvailableTypes, nameof(RingFamilyDisplay), nameof(RingAvailableTypes), nameof(RingFamilyType)),
                                                    _ => HasFamilySelection(_ringFamilyPath, _ringFamilyType, RingAvailableTypes));
            ClearStrapCommand    = new RelayCommand(_ => ClearFamily(ref _strapFamilyPath, ref _strapFamilyType, StrapAvailableTypes, nameof(StrapFamilyDisplay), nameof(StrapAvailableTypes), nameof(StrapFamilyType)),
                                                    _ => HasFamilySelection(_strapFamilyPath, _strapFamilyType, StrapAvailableTypes));
            ClearLifelineCommand = new RelayCommand(_ => ClearFamily(ref _lifelineFamilyPath, ref _lifelineFamilyType, LifelineAvailableTypes, nameof(LifelineFamilyDisplay), nameof(LifelineAvailableTypes), nameof(LifelineFamilyType)),
                                                    _ => HasFamilySelection(_lifelineFamilyPath, _lifelineFamilyType, LifelineAvailableTypes));

            SavePresetCommand   = new RelayCommand(_ => SavePreset(),   _ => !string.IsNullOrWhiteSpace(PresetName));
            LoadPresetCommand   = new RelayCommand(_ => LoadPreset(),   _ => !string.IsNullOrWhiteSpace(PresetName));
            DeletePresetCommand = new RelayCommand(_ => DeletePreset(), _ => !string.IsNullOrWhiteSpace(PresetName));

            RefreshPresets();
            if (IsEditMode) LoadEditContext();
            else TryLoadLast();
            SagaLog.Write("LadderViewModel — construtor OK");
        }

        // ── Modo / títulos ─────────────────────────────────────────────────

        public bool IsEditMode => _editContext != null;
        public bool CanClose   => !_isSubmitting;
        public string CreateActionText => IsEditMode ? "Atualizar" : "Criar";
        public string WindowTitle => IsEditMode
            ? "SAGA — Editar Escada Marinheiro"
            : "SAGA — Gerar Escada Marinheiro";
        public string PickReferenceActionText => IsEditMode
            ? "Substituir referência"
            : "+ Selecionar viga de referência";

        // ── Referência superior (UC-01) ────────────────────────────────────

        public bool HasPlacement => _placement != null && _placement.IsValid;
        public string PlacementSummary => ResolvePlacement()?.Summary ?? "Nenhuma referência selecionada.";

        // ── Modo de definição da base ──────────────────────────────────────
        // 0 = Automático (nível mais próximo abaixo do desembarque, como hoje)
        // 1 = Nível existente escolhido manualmente
        // 2 = Altura manual (ignora níveis, usa a distância informada)

        private int    _baseModeIndex;
        private string _selectedLevelName;
        private double _manualHeightMm; // preenchido com a altura auto-detectada ao carregar a referência

        public int BaseModeIndex
        {
            get => _baseModeIndex;
            set
            {
                if (Set(ref _baseModeIndex, value))
                {
                    IsCalculated = false;
                    OnPropertyChanged(nameof(IsLevelModeVisible));
                    OnPropertyChanged(nameof(IsManualHeightModeVisible));
                    OnPropertyChanged(nameof(PlacementSummary));
                }
            }
        }
        public bool IsLevelModeVisible        => BaseModeIndex == 1;
        public bool IsManualHeightModeVisible => BaseModeIndex == 2;

        public string SelectedLevelName
        {
            get => _selectedLevelName;
            set { if (Set(ref _selectedLevelName, value)) { IsCalculated = false; OnPropertyChanged(nameof(PlacementSummary)); } }
        }
        public double ManualHeightMm
        {
            get => _manualHeightMm;
            set { if (Set(ref _manualHeightMm, value)) { IsCalculated = false; OnPropertyChanged(nameof(PlacementSummary)); } }
        }

        public ObservableCollection<string> AvailableLevelNames { get; } = new ObservableCollection<string>();

        private void RefreshAvailableLevels()
        {
            AvailableLevelNames.Clear();
            foreach (var lvl in _placement?.AvailableLevels ?? Enumerable.Empty<LadderLevelOption>())
                AvailableLevelNames.Add(lvl.Name);

            _selectedLevelName = AvailableLevelNames.Contains(_placement?.LevelName)
                ? _placement.LevelName
                : AvailableLevelNames.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedLevelName));

            if (_manualHeightMm <= 0 && _placement != null)
                _manualHeightMm = _placement.HeightMm;
            OnPropertyChanged(nameof(ManualHeightMm));
        }

        /// <summary>
        /// Reference original (BaseModeIndex == 0) ou uma cópia com BaseZFt/LevelName
        /// substituídos conforme o modo escolhido — usada pra preview e pra criação,
        /// sem nunca perder o valor auto-detectado original de <see cref="_placement"/>.
        /// </summary>
        private LadderPlacement ResolvePlacement()
        {
            if (_placement == null) return null;
            if (BaseModeIndex == 0) return _placement;

            var resolved = new LadderPlacement
            {
                InsertionPoint  = _placement.InsertionPoint,
                Lateral         = _placement.Lateral,
                TopZFt          = _placement.TopZFt,
                BeamHalfWidthMm = _placement.BeamHalfWidthMm,
                BeamName        = _placement.BeamName,
                BeamId          = _placement.BeamId,
                AvailableLevels = _placement.AvailableLevels
            };

            if (BaseModeIndex == 1)
            {
                var level = _placement.AvailableLevels?.FirstOrDefault(l =>
                    string.Equals(l.Name, SelectedLevelName, StringComparison.Ordinal));
                resolved.BaseZFt   = level?.ElevationFt ?? _placement.BaseZFt;
                resolved.LevelName = level?.Name ?? _placement.LevelName;
            }
            else // 2 — altura manual
            {
                resolved.BaseZFt   = _placement.TopZFt - Math.Max(ManualHeightMm, 0) / 304.8;
                resolved.LevelName = "(altura manual)";
            }

            return resolved;
        }

        private void OnPlacementPicked(LadderPlacement placement)
        {
            _dispatcher.Invoke(() =>
            {
                _placement   = placement;
                IsCalculated = false;
                RefreshAvailableLevels();
                OnPropertyChanged(nameof(HasPlacement));
                OnPropertyChanged(nameof(PlacementSummary));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            });
        }

        private void OnPickFailed(string reason)
        {
            _dispatcher.Invoke(() =>
            {
                Warnings.Clear();
                Warnings.Add(reason);
            });
        }

        private void LoadEditContext()
        {
            if (_editContext?.Config == null || _editContext.Placement == null) return;

            ApplyConfig(_editContext.Config);
            _placement = _editContext.Placement;
            if (_placement.AvailableLevels == null && _editContext.SourceDocument != null)
            {
                _placement.AvailableLevels = new Autodesk.Revit.DB.FilteredElementCollector(_editContext.SourceDocument)
                    .OfClass(typeof(Autodesk.Revit.DB.Level))
                    .Cast<Autodesk.Revit.DB.Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => new LadderLevelOption { Name = l.Name, ElevationFt = l.Elevation })
                    .ToList();
            }
            RefreshAvailableLevels();
            OnPropertyChanged(nameof(HasPlacement));
            OnPropertyChanged(nameof(PlacementSummary));

            Warnings.Clear();
            Warnings.Add("Editando escada marinheiro existente. Altere os campos e clique em Atualizar.");
            Warnings.Add("A atualização recria as peças; cotas, tags ou restrições ligadas a elas podem perder o vínculo.");
        }

        // ── Globais ────────────────────────────────────────────────────────

        private double _topLevelOffset = LadderDefaults.TopLevelOffset;
        private double _baseOffset     = LadderDefaults.BaseOffset;
        private double _wallOffset     = LadderDefaults.WallOffset;

        public double TopLevelOffset { get => _topLevelOffset; set { if (Set(ref _topLevelOffset, value)) IsCalculated = false; } }
        public double BaseOffset     { get => _baseOffset;     set { if (Set(ref _baseOffset, value)) IsCalculated = false; } }
        public double WallOffset     { get => _wallOffset;     set { if (Set(ref _wallOffset, value)) IsCalculated = false; } }

        // ── Corpo ──────────────────────────────────────────────────────────

        private double _width       = LadderDefaults.Width;
        private double _rungSpacing = LadderDefaults.RungSpacing;

        public double Width       { get => _width;       set { if (Set(ref _width, value)) IsCalculated = false; } }
        public double RungSpacing { get => _rungSpacing; set { if (Set(ref _rungSpacing, value)) IsCalculated = false; } }

        private string _stringerFamilyPath;
        private string _stringerFamilyType;
        public string StringerFamilyDisplay => FamilyDisplay(_stringerFamilyPath, _stringerFamilyType, required: true);
        public string StringerFamilyType { get => _stringerFamilyType; set { if (Set(ref _stringerFamilyType, value)) OnPropertyChanged(nameof(StringerFamilyDisplay)); } }
        public ObservableCollection<string> StringerAvailableTypes { get; } = new ObservableCollection<string>();

        private string _rungFamilyPath;
        private string _rungFamilyType;
        public string RungFamilyDisplay => FamilyDisplay(_rungFamilyPath, _rungFamilyType, required: true);
        public string RungFamilyType { get => _rungFamilyType; set { if (Set(ref _rungFamilyType, value)) OnPropertyChanged(nameof(RungFamilyDisplay)); } }

        // Unificação de perfis: "todos" (suporte+anel+tira usam o do montante) tem
        // prioridade sobre "gaiola" (suporte+anel+tira usam o do suporte). Degrau
        // nunca entra nesses agrupamentos — sempre com família própria.
        private bool _sameProfileAll;
        public bool SameProfileAll
        {
            get => _sameProfileAll;
            set
            {
                if (Set(ref _sameProfileAll, value))
                {
                    IsCalculated = false;
                    OnPropertyChanged(nameof(IsSupportFamilyPickerVisible));
                    OnPropertyChanged(nameof(IsRingFamilyPickerVisible));
                    OnPropertyChanged(nameof(IsStrapFamilyPickerVisible));
                    OnPropertyChanged(nameof(IsSameProfileCageEnabled));
                }
            }
        }
        public ObservableCollection<string> RungAvailableTypes { get; } = new ObservableCollection<string>();

        // ── Suportes ───────────────────────────────────────────────────────

        private string _supportFamilyPath;
        private string _supportFamilyType;
        private double _supportMaxSpacing = LadderDefaults.SupportMaxSpacing;
        public string SupportFamilyDisplay => FamilyDisplay(_supportFamilyPath, _supportFamilyType, required: false);
        public string SupportFamilyType { get => _supportFamilyType; set { if (Set(ref _supportFamilyType, value)) OnPropertyChanged(nameof(SupportFamilyDisplay)); } }
        public ObservableCollection<string> SupportAvailableTypes { get; } = new ObservableCollection<string>();
        public double SupportMaxSpacing { get => _supportMaxSpacing; set { if (Set(ref _supportMaxSpacing, value)) IsCalculated = false; } }

        private bool _sameProfileCage;
        public bool SameProfileCage
        {
            get => _sameProfileCage;
            set
            {
                if (Set(ref _sameProfileCage, value))
                {
                    IsCalculated = false;
                    OnPropertyChanged(nameof(IsRingFamilyPickerVisible));
                    OnPropertyChanged(nameof(IsStrapFamilyPickerVisible));
                }
            }
        }

        public bool IsSameProfileCageEnabled  => !SameProfileAll;
        public bool IsSupportFamilyPickerVisible => !SameProfileAll;
        public bool IsRingFamilyPickerVisible    => !SameProfileAll && !SameProfileCage;
        public bool IsStrapFamilyPickerVisible   => !SameProfileAll && !SameProfileCage;

        // ── Gaiola ─────────────────────────────────────────────────────────

        private bool   _hasCage;
        private double _cageStartHeight = LadderDefaults.CageStartHeight;
        private double _cageProjection  = LadderDefaults.CageProjection;
        private double _ringSetback     = LadderDefaults.RingSetback;
        private int    _ringModeIndex;                       // 0=Equidistante, 1=Passo fixo
        private double _ringSpacing = LadderDefaults.RingSpacing;
        private double _strapAngleStepDeg = LadderDefaults.StrapAngleStepDeg;
        private int    _strapCount = LadderDefaults.StrapCount;

        public bool HasCage
        {
            get => _hasCage;
            set { if (Set(ref _hasCage, value)) { IsCalculated = false; OnPropertyChanged(nameof(IsCageEnabled)); } }
        }
        public bool IsCageEnabled => HasCage;
        public double CageStartHeight { get => _cageStartHeight; set { if (Set(ref _cageStartHeight, value)) IsCalculated = false; } }
        public double CageProjection  { get => _cageProjection;  set { if (Set(ref _cageProjection, value)) IsCalculated = false; } }
        public double RingSetback     { get => _ringSetback;     set { if (Set(ref _ringSetback, value)) IsCalculated = false; } }
        public int RingModeIndex      { get => _ringModeIndex;   set { if (Set(ref _ringModeIndex, value)) IsCalculated = false; } }
        public double RingSpacing     { get => _ringSpacing;     set { if (Set(ref _ringSpacing, value)) IsCalculated = false; } }
        public double StrapAngleStepDeg { get => _strapAngleStepDeg; set { if (Set(ref _strapAngleStepDeg, value)) IsCalculated = false; } }
        public int StrapCount         { get => _strapCount;      set { if (Set(ref _strapCount, value)) IsCalculated = false; } }

        private string _ringFamilyPath;
        private string _ringFamilyType;
        public string RingFamilyDisplay => FamilyDisplay(_ringFamilyPath, _ringFamilyType, required: false);
        public string RingFamilyType { get => _ringFamilyType; set { if (Set(ref _ringFamilyType, value)) OnPropertyChanged(nameof(RingFamilyDisplay)); } }
        public ObservableCollection<string> RingAvailableTypes { get; } = new ObservableCollection<string>();

        private string _strapFamilyPath;
        private string _strapFamilyType;
        public string StrapFamilyDisplay => FamilyDisplay(_strapFamilyPath, _strapFamilyType, required: false);
        public string StrapFamilyType { get => _strapFamilyType; set { if (Set(ref _strapFamilyType, value)) OnPropertyChanged(nameof(StrapFamilyDisplay)); } }
        public ObservableCollection<string> StrapAvailableTypes { get; } = new ObservableCollection<string>();

        // ── Linha de vida ──────────────────────────────────────────────────

        private bool _hasLifeline;
        public bool HasLifeline
        {
            get => _hasLifeline;
            set { if (Set(ref _hasLifeline, value)) { IsCalculated = false; OnPropertyChanged(nameof(IsLifelineEnabled)); } }
        }
        public bool IsLifelineEnabled => HasLifeline;

        private string _lifelineFamilyPath;
        private string _lifelineFamilyType;
        public string LifelineFamilyDisplay => FamilyDisplay(_lifelineFamilyPath, _lifelineFamilyType, required: false);
        public string LifelineFamilyType { get => _lifelineFamilyType; set { if (Set(ref _lifelineFamilyType, value)) OnPropertyChanged(nameof(LifelineFamilyDisplay)); } }
        public ObservableCollection<string> LifelineAvailableTypes { get; } = new ObservableCollection<string>();

        // ── Desembarque ────────────────────────────────────────────────────

        private double _extensionHeight = LadderDefaults.ExtensionHeight;
        private double _exitFlare       = LadderDefaults.ExitFlare;
        private double _exitKinkHeight  = LadderDefaults.ExitKinkHeight;
        public double ExtensionHeight { get => _extensionHeight; set { if (Set(ref _extensionHeight, value)) IsCalculated = false; } }
        public double ExitFlare       { get => _exitFlare;       set { if (Set(ref _exitFlare, value)) IsCalculated = false; } }
        public double ExitKinkHeight  { get => _exitKinkHeight;  set { if (Set(ref _exitKinkHeight, value)) IsCalculated = false; } }

        // ── Preview ────────────────────────────────────────────────────────

        private bool _isCalculated;
        public bool IsCalculated { get => _isCalculated; set => Set(ref _isCalculated, value); }
        public ObservableCollection<string> Warnings { get; } = new ObservableCollection<string>();

        public double PreviewHeight       => _definition?.TotalHeightMm ?? 0;
        public int    PreviewRungCount    => _definition?.RungCount ?? 0;
        public double PreviewBottomGap    => _definition?.BottomGapMm ?? 0;
        public int    PreviewSupportCount => _definition?.SupportCount ?? 0;
        public int    PreviewRingCount    => _definition?.RingCount ?? 0;
        public int    PreviewStrapCount   => _definition?.StrapCount ?? 0;

        private double CurrentHeightMm
        {
            get
            {
                var p = ResolvePlacement();
                return p == null
                    ? 0
                    : (p.TopZFt + TopLevelOffset / 304.8 - p.BaseZFt - BaseOffset / 304.8) * 304.8;
            }
        }

        private void CalculatePreview()
        {
            try
            {
                _definition  = LadderCalculator.Calculate(CurrentHeightMm, BuildConfig());
                IsCalculated = true;

                OnPropertyChanged(nameof(PreviewHeight));
                OnPropertyChanged(nameof(PreviewRungCount));
                OnPropertyChanged(nameof(PreviewBottomGap));
                OnPropertyChanged(nameof(PreviewSupportCount));
                OnPropertyChanged(nameof(PreviewRingCount));
                OnPropertyChanged(nameof(PreviewStrapCount));
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

        // ── Criação ────────────────────────────────────────────────────────

        private void CreateLadder()
        {
            CalculatePreview();
            if (_definition == null || !_definition.IsValid)
                return;   // avisos já preenchidos pelo cálculo

            var config = BuildConfig();
            _createHandler.Placement   = ResolvePlacement();
            _createHandler.Config      = config;
            _createHandler.Definition  = _definition;
            _createHandler.EditContext = _editContext;

            LadderPresetStore.SaveLast(config);

            _isSubmitting = true;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            _createEvent.Raise();
        }

        private void OnCreationCompleted(string error)
        {
            _dispatcher.Invoke(() =>
            {
                _isSubmitting = false;
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();

                if (error == null)
                {
                    Warnings.Clear();
                    Warnings.Add(IsEditMode
                        ? "Escada marinheiro atualizada com sucesso."
                        : "Escada marinheiro criada com sucesso.");
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Erro ao criar escada marinheiro:\n\n{error}",
                        "SAGA Structural Tools",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            });
        }

        // ── Presets ────────────────────────────────────────────────────────

        public ObservableCollection<string> Presets { get; } = new ObservableCollection<string>();

        private string _presetName;
        public string PresetName { get => _presetName; set => Set(ref _presetName, value); }

        private void RefreshPresets()
        {
            Presets.Clear();
            foreach (var n in LadderPresetStore.List()) Presets.Add(n);
        }

        private void TryLoadLast()
        {
            var last = LadderPresetStore.LoadLast();
            if (last != null) ApplyConfig(last);
        }

        private void SavePreset()
        {
            LadderPresetStore.Save(PresetName.Trim(), BuildConfig());
            RefreshPresets();
            Warnings.Clear();
            Warnings.Add($"Configuração '{PresetName.Trim()}' salva.");
        }

        private void LoadPreset()
        {
            var c = LadderPresetStore.Load(PresetName.Trim());
            if (c == null)
            {
                Warnings.Clear();
                Warnings.Add($"Configuração '{PresetName.Trim()}' não encontrada.");
                return;
            }
            ApplyConfig(c);
            Warnings.Clear();
            Warnings.Add($"Configuração '{PresetName.Trim()}' carregada.");
        }

        private void DeletePreset()
        {
            LadderPresetStore.Delete(PresetName.Trim());
            RefreshPresets();
            Warnings.Clear();
            Warnings.Add($"Configuração '{PresetName.Trim()}' excluída.");
        }

        // ── Config ↔ campos ────────────────────────────────────────────────

        private LadderConfig BuildConfig() => new LadderConfig
        {
            TopLevelOffset     = TopLevelOffset,
            BaseOffset         = BaseOffset,
            WallOffset         = WallOffset,

            Width              = Width,
            RungSpacing        = RungSpacing,
            StringerFamilyPath = _stringerFamilyPath,
            StringerFamilyType = _stringerFamilyType,
            RungFamilyPath     = _rungFamilyPath,
            RungFamilyType     = _rungFamilyType,
            SameProfileAll     = SameProfileAll,

            SupportFamilyPath  = _supportFamilyPath,
            SupportFamilyType  = _supportFamilyType,
            SupportMaxSpacing  = SupportMaxSpacing,
            SameProfileCage    = SameProfileCage,

            HasCage            = HasCage,
            CageStartHeight    = CageStartHeight,
            CageProjection     = CageProjection,
            RingSetback        = RingSetback,
            RingMode           = RingModeIndex == 1 ? RingDistribution.FixedSpacing : RingDistribution.Equidistant,
            RingSpacing        = RingSpacing,
            RingFamilyPath     = _ringFamilyPath,
            RingFamilyType     = _ringFamilyType,
            StrapAngleStepDeg  = StrapAngleStepDeg,
            StrapCount         = StrapCount,
            StrapFamilyPath    = _strapFamilyPath,
            StrapFamilyType    = _strapFamilyType,

            HasLifeline        = HasLifeline,
            LifelineFamilyPath = _lifelineFamilyPath,
            LifelineFamilyType = _lifelineFamilyType,

            ExtensionHeight    = ExtensionHeight,
            ExitFlare          = ExitFlare,
            ExitKinkHeight     = ExitKinkHeight
        };

        private void ApplyConfig(LadderConfig c)
        {
            if (c == null) return;

            TopLevelOffset = c.TopLevelOffset;
            BaseOffset     = c.BaseOffset;
            WallOffset     = c.WallOffset;

            Width       = c.Width;
            RungSpacing = c.RungSpacing;
            SetFamily(ref _stringerFamilyPath, ref _stringerFamilyType, c.StringerFamilyPath, c.StringerFamilyType,
                      StringerAvailableTypes, nameof(StringerFamilyDisplay), nameof(StringerAvailableTypes), nameof(StringerFamilyType));
            SetFamily(ref _rungFamilyPath, ref _rungFamilyType, c.RungFamilyPath, c.RungFamilyType,
                      RungAvailableTypes, nameof(RungFamilyDisplay), nameof(RungAvailableTypes), nameof(RungFamilyType));
            SameProfileAll = c.SameProfileAll;

            SetFamily(ref _supportFamilyPath, ref _supportFamilyType, c.SupportFamilyPath, c.SupportFamilyType,
                      SupportAvailableTypes, nameof(SupportFamilyDisplay), nameof(SupportAvailableTypes), nameof(SupportFamilyType));
            SupportMaxSpacing = c.SupportMaxSpacing;
            SameProfileCage   = c.SameProfileCage;

            HasCage         = c.HasCage;
            CageStartHeight = c.CageStartHeight;
            CageProjection  = c.CageProjection;
            RingSetback     = c.RingSetback;
            RingModeIndex   = c.RingMode == RingDistribution.FixedSpacing ? 1 : 0;
            RingSpacing     = c.RingSpacing;
            StrapAngleStepDeg = c.StrapAngleStepDeg;
            StrapCount        = c.StrapCount;
            SetFamily(ref _ringFamilyPath, ref _ringFamilyType, c.RingFamilyPath, c.RingFamilyType,
                      RingAvailableTypes, nameof(RingFamilyDisplay), nameof(RingAvailableTypes), nameof(RingFamilyType));
            SetFamily(ref _strapFamilyPath, ref _strapFamilyType, c.StrapFamilyPath, c.StrapFamilyType,
                      StrapAvailableTypes, nameof(StrapFamilyDisplay), nameof(StrapAvailableTypes), nameof(StrapFamilyType));

            HasLifeline = c.HasLifeline;
            SetFamily(ref _lifelineFamilyPath, ref _lifelineFamilyType, c.LifelineFamilyPath, c.LifelineFamilyType,
                      LifelineAvailableTypes, nameof(LifelineFamilyDisplay), nameof(LifelineAvailableTypes), nameof(LifelineFamilyType));

            ExtensionHeight = c.ExtensionHeight;
            ExitFlare       = c.ExitFlare;
            ExitKinkHeight  = c.ExitKinkHeight;
        }

        // ── Helpers de família (mesmos padrões do RailViewModel) ───────────

        private void SetFamily(ref string pathField, ref string typeField, string path, string type,
                               ObservableCollection<string> types,
                               string displayProp, string typesProp, string typeProp)
        {
            pathField = path;
            types.Clear();
            typeField = type;
            if (!string.IsNullOrWhiteSpace(path))
                LoadTypesFromCatalog(path, type, types, out typeField);
            OnPropertyChanged(displayProp);
            OnPropertyChanged(typesProp);
            OnPropertyChanged(typeProp);
        }

        private void BrowseFamily(ref string pathField, ref string typeField,
                                  string displayProp, string typesProp, string typeProp,
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
                LoadTypesFromCatalog(dlg.FileName, null, typesCollection, out typeField);
                OnPropertyChanged(displayProp);
                OnPropertyChanged(typesProp);
                OnPropertyChanged(typeProp);
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        private void ClearFamily(ref string pathField, ref string typeField,
                                 ObservableCollection<string> typesCollection,
                                 string displayProp, string typesProp, string typeProp)
        {
            pathField = null;
            typeField = null;
            typesCollection.Clear();
            OnPropertyChanged(displayProp);
            OnPropertyChanged(typesProp);
            OnPropertyChanged(typeProp);
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        private static bool HasFamilySelection(string path, string type,
                                               ObservableCollection<string> typesCollection)
        {
            return !string.IsNullOrWhiteSpace(path) ||
                   !string.IsNullOrWhiteSpace(type) ||
                   (typesCollection?.Count ?? 0) > 0;
        }

        private static void LoadTypesFromCatalog(string rfaPath, string currentType,
                                                 ObservableCollection<string> target, out string selectedType)
        {
            target.Clear();
            selectedType = currentType;

            var catalogPath = Path.ChangeExtension(rfaPath, ".txt");
            if (!File.Exists(catalogPath))
            {
                if (!string.IsNullOrWhiteSpace(currentType)) target.Add(currentType);
                return;
            }

            try
            {
                var lines = CatalogTextReader.ReadAllLines(catalogPath);
                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("##")) continue;
                    var name = line.Split(',')[0].Trim().Replace("\"\"", "\"");
                    if (!string.IsNullOrWhiteSpace(name) && !name.Contains("##"))
                        target.Add(name);
                }
                if (target.Count > 0)
                {
                    selectedType = target.FirstOrDefault(t =>
                        string.Equals(t, currentType, StringComparison.OrdinalIgnoreCase));
                    if (selectedType == null)
                    {
                        if (string.IsNullOrWhiteSpace(currentType))
                        {
                            selectedType = target[0];
                        }
                        else
                        {
                            target.Insert(0, currentType);
                            selectedType = currentType;
                        }
                    }
                }
            }
            catch
            {
                target.Clear();
                if (!string.IsNullOrWhiteSpace(currentType)) target.Add(currentType);
                selectedType = currentType;
            }
        }

        private static string FamilyDisplay(string path, string type, bool required)
        {
            if (string.IsNullOrWhiteSpace(path))
                return required ? "Selecione uma família (.rfa)" : "Sem família — não será criado";
            var name = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(type) ? name : $"{name}  ·  {type}";
        }

        // ── Commands ───────────────────────────────────────────────────────

        public RelayCommand PickReferenceCommand    { get; }
        public RelayCommand CalculatePreviewCommand { get; }
        public RelayCommand CreateCommand           { get; }

        public RelayCommand BrowseStringerCommand   { get; }
        public RelayCommand BrowseRungCommand       { get; }
        public RelayCommand BrowseSupportCommand    { get; }
        public RelayCommand BrowseRingCommand       { get; }
        public RelayCommand BrowseStrapCommand      { get; }
        public RelayCommand BrowseLifelineCommand   { get; }

        public RelayCommand ClearSupportCommand     { get; }
        public RelayCommand ClearRingCommand        { get; }
        public RelayCommand ClearStrapCommand       { get; }
        public RelayCommand ClearLifelineCommand    { get; }

        public RelayCommand SavePresetCommand       { get; }
        public RelayCommand LoadPresetCommand       { get; }
        public RelayCommand DeletePresetCommand     { get; }
    }
}
