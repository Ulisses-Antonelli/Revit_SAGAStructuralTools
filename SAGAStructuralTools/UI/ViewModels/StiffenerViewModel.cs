using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Stiffener;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class StiffenerViewModel : ViewModelBase, IDisposable
    {
        private readonly Dispatcher                    _dispatcher;
        private readonly ExternalEvent                  _pickEvent;
        private readonly ExternalEvent                  _createEvent;
        private readonly StiffenerCreationHandler        _createHandler;
        private readonly StiffenerEditContext            _editContext;
        private readonly ExternalEvent                  _alignmentPickEvent;
        private readonly StiffenerAlignmentPickHandler   _alignmentPickHandler;
        private bool                                _isSubmitting;

        private StiffenerPlacement  _placement;
        private StiffenerDefinition _definition;

        private double _plateThicknessMm  = StiffenerDefaults.PlateThickness;
        private double _chamferOverrideMm = 0;
        private bool   _symmetric         = true;

        public StiffenerViewModel(ExternalEvent pickEvent, StiffenerPickHandler pickHandler,
                                  ExternalEvent createEvent, StiffenerCreationHandler createHandler,
                                  StiffenerEditContext editContext = null)
        {
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

            _alignmentPickHandler = new StiffenerAlignmentPickHandler();
            _alignmentPickHandler.ReferencePicked += OnReferencePicked;
            _alignmentPickHandler.PickFailed      += OnPickFailed;
            _alignmentPickEvent = ExternalEvent.Create(_alignmentPickHandler);

            PickBeamCommand         = new RelayCommand(_ => _pickEvent.Raise(), _ => !_isSubmitting);
            CalculatePreviewCommand = new RelayCommand(_ => CalculatePreview(), _ => HasPlacement);
            CreateCommand           = new RelayCommand(_ => CreateStiffener(), _ => HasPlacement && !_isSubmitting);
            SelectAlignmentReferenceCommand = new RelayCommand(_ => _alignmentPickEvent.Raise(), _ => HasPlacement && !_isSubmitting);
            ClearAlignmentReferenceCommand  = new RelayCommand(_ => ClearAlignmentReference(), _ => HasAlignReference);

            if (IsEditMode) LoadEditContext();
        }

        public void Dispose() => _alignmentPickEvent?.Dispose();

        // ── Edição ─────────────────────────────────────────────────────────

        public bool   IsEditMode        => _editContext != null;
        public string WindowTitle       => IsEditMode ? "SAGA — Editar Nervura" : "SAGA — Gerar Nervura em Perfil W";
        public string CreateActionText  => IsEditMode ? "Atualizar nervura" : "Gerar nervura";

        private void LoadEditContext()
        {
            if (_editContext?.Config == null || _editContext.Placement == null) return;

            _plateThicknessMm  = _editContext.Config.PlateThickness;
            _chamferOverrideMm = _editContext.Config.ChamferOverride;
            _symmetric         = _editContext.Config.Symmetric;
            _placement         = _editContext.Placement;

            OnPropertyChanged(nameof(PlateThicknessMm));
            OnPropertyChanged(nameof(ChamferOverrideMm));
            OnPropertyChanged(nameof(Symmetric));
            OnPropertyChanged(nameof(HasPlacement));
            OnPropertyChanged(nameof(BeamName));
            OnPropertyChanged(nameof(HasAlignReference));
            OnPropertyChanged(nameof(AlignReferenceName));
            CalculatePreview();
        }

        // ── Seleção ────────────────────────────────────────────────────────

        public bool   HasPlacement => _placement != null;
        public string BeamName     => _placement?.BeamName ?? "(nenhuma viga selecionada)";

        private void OnPlacementPicked(StiffenerPlacement placement)
        {
            _dispatcher.Invoke(() =>
            {
                _placement = placement;
                OnPropertyChanged(nameof(HasPlacement));
                OnPropertyChanged(nameof(BeamName));
                CommandManager.InvalidateRequerySuggested();
                CalculatePreview();
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

        // ── Referência de alinhamento (opcional) ──────────────────────────────

        public bool   HasAlignReference => _placement?.AlignFacePoint != null;
        public string AlignReferenceName => _placement?.AlignReferenceName;

        private void OnReferencePicked(Autodesk.Revit.DB.XYZ point, string referenceName)
        {
            _dispatcher.Invoke(() =>
            {
                if (!HasPlacement)
                {
                    Warnings.Clear();
                    Warnings.Add("Selecione a viga antes de escolher a referência de alinhamento.");
                    return;
                }

                _placement.AlignFacePoint     = point;
                _placement.AlignReferenceName = referenceName;
                OnPropertyChanged(nameof(HasAlignReference));
                OnPropertyChanged(nameof(AlignReferenceName));
                CommandManager.InvalidateRequerySuggested();
            });
        }

        private void ClearAlignmentReference()
        {
            if (_placement == null) return;
            _placement.AlignFacePoint     = null;
            _placement.AlignReferenceName = null;
            OnPropertyChanged(nameof(HasAlignReference));
            OnPropertyChanged(nameof(AlignReferenceName));
            CommandManager.InvalidateRequerySuggested();
        }

        // ── Configuração ───────────────────────────────────────────────────

        public double PlateThicknessMm
        {
            get => _plateThicknessMm;
            set { if (Set(ref _plateThicknessMm, value)) CalculatePreview(); }
        }

        /// <summary>0 = calcular automaticamente a partir do raio de concordância do perfil.</summary>
        public double ChamferOverrideMm
        {
            get => _chamferOverrideMm;
            set { if (Set(ref _chamferOverrideMm, value)) CalculatePreview(); }
        }

        /// <summary>true = uma chapa espelhada de cada lado da alma; false = só do lado clicado.</summary>
        public bool Symmetric
        {
            get => _symmetric;
            set { if (Set(ref _symmetric, value)) CalculatePreview(); }
        }

        public ObservableCollection<string> Warnings { get; } = new ObservableCollection<string>();

        // ── Preview ────────────────────────────────────────────────────────

        public string PreviewChamfer => _definition != null && _definition.IsValid
            ? $"{_definition.ChamferMm:F1} mm ({_definition.ChamferSource})"
            : "—";

        public string PreviewPlateCount => _definition != null && _definition.IsValid
            ? $"{_definition.Plates.Count} chapa(s)"
            : "—";

        private void CalculatePreview()
        {
            if (!HasPlacement) return;
            try
            {
                var config = BuildConfig();
                _definition = StiffenerCalculator.Calculate(
                    _placement.HeightMm, _placement.WidthMm,
                    _placement.FlangeThicknessMm, _placement.WebThicknessMm,
                    _placement.FilletRadiusMm, _placement.PreferredSide, config);

                OnPropertyChanged(nameof(PreviewChamfer));
                OnPropertyChanged(nameof(PreviewPlateCount));
                CommandManager.InvalidateRequerySuggested();

                Warnings.Clear();
                foreach (var w in _definition.Warnings) Warnings.Add(w);
            }
            catch (Exception ex)
            {
                Warnings.Clear();
                Warnings.Add($"Erro no cálculo: {ex.Message}");
            }
        }

        private StiffenerConfig BuildConfig() => new StiffenerConfig
        {
            PlateThickness  = PlateThicknessMm,
            ChamferOverride = ChamferOverrideMm,
            Symmetric       = Symmetric
        };

        // ── Criação ────────────────────────────────────────────────────────

        private void CreateStiffener()
        {
            CalculatePreview();
            if (_definition == null || !_definition.IsValid) return;

            _createHandler.Placement   = _placement;
            _createHandler.Config      = BuildConfig();
            _createHandler.Definition  = _definition;
            _createHandler.EditContext = _editContext;

            _isSubmitting = true;
            CommandManager.InvalidateRequerySuggested();
            _createEvent.Raise();
        }

        private void OnCreationCompleted(string error)
        {
            _dispatcher.Invoke(() =>
            {
                _isSubmitting = false;
                CommandManager.InvalidateRequerySuggested();
                OnPropertyChanged(nameof(CanClose));

                if (error == null)
                {
                    Warnings.Clear();
                    Warnings.Add(IsEditMode
                        ? "Nervura atualizada com sucesso."
                        : "Nervura criada com sucesso.");
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Erro ao criar nervura:\n\n{error}",
                        "SAGA Structural Tools",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            });
        }

        public bool CanClose => !_isSubmitting;

        public RelayCommand PickBeamCommand                 { get; }
        public RelayCommand CalculatePreviewCommand         { get; }
        public RelayCommand CreateCommand                   { get; }
        public RelayCommand SelectAlignmentReferenceCommand { get; }
        public RelayCommand ClearAlignmentReferenceCommand  { get; }
    }
}
