using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.EndPlate;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class EndPlateViewModel : ViewModelBase
    {
        private readonly Dispatcher                 _dispatcher;
        private readonly ExternalEvent               _pickSingleEvent;
        private readonly ExternalEvent               _pickTwoEvent;
        private readonly ExternalEvent               _createEvent;
        private readonly EndPlateCreationHandler      _createHandler;
        private readonly EndPlateEditContext          _editContext;
        private bool                                 _isSubmitting;
        private bool                                 _definitionsValid;

        private EndPlatePlacement _placement;

        private double _plateThicknessMm   = EndPlateDefaults.PlateThickness;
        private double _marginVerticalMm   = EndPlateDefaults.MarginVertical;
        private double _marginHorizontalMm = EndPlateDefaults.MarginHorizontal;
        private bool   _generateBothMembers = true;
        private double _gapBetweenPlatesMm = EndPlateDefaults.GapBetweenPlates;

        public EndPlateViewModel(ExternalEvent pickSingleEvent, EndPlatePickHandler pickSingleHandler,
                                 ExternalEvent pickTwoEvent, EndPlateTwoPickHandler pickTwoHandler,
                                 ExternalEvent createEvent, EndPlateCreationHandler createHandler,
                                 EndPlateEditContext editContext = null)
        {
            _dispatcher      = Dispatcher.CurrentDispatcher;
            _pickSingleEvent = pickSingleEvent;
            _pickTwoEvent    = pickTwoEvent;
            _createEvent     = createEvent;
            _createHandler   = createHandler;
            _editContext     = editContext;

            if (pickSingleHandler != null)
            {
                pickSingleHandler.MemberPicked += OnMemberPicked;
                pickSingleHandler.PickFailed   += OnPickFailed;
            }
            if (pickTwoHandler != null)
            {
                pickTwoHandler.MembersPicked += OnMemberPicked;
                pickTwoHandler.PickFailed    += OnPickFailed;
            }
            _createHandler.Completed += OnCreationCompleted;

            PickSingleCommand       = new RelayCommand(_ => _pickSingleEvent.Raise(), _ => !_isSubmitting);
            PickTwoCommand          = new RelayCommand(_ => _pickTwoEvent.Raise(), _ => !_isSubmitting);
            CalculatePreviewCommand = new RelayCommand(_ => CalculatePreview(), _ => HasPlacement);
            CreateCommand           = new RelayCommand(_ => CreateEndPlate(), _ => HasPlacement && !_isSubmitting);

            if (IsEditMode) LoadEditContext();
        }

        // ── Edição ─────────────────────────────────────────────────────────

        public bool   IsEditMode       => _editContext != null;
        public string WindowTitle      => IsEditMode ? "SAGA — Editar Chapa de Topo" : "SAGA — Gerar Chapa de Topo (End Plate)";
        public string CreateActionText => IsEditMode ? "Atualizar chapa" : "Gerar chapa";

        private void LoadEditContext()
        {
            if (_editContext?.Config == null || _editContext.Placement == null) return;

            _plateThicknessMm   = _editContext.Config.PlateThickness;
            _marginVerticalMm   = _editContext.Config.MarginVertical;
            _marginHorizontalMm = _editContext.Config.MarginHorizontal;
            _generateBothMembers = _editContext.Config.GenerateBothMembers;
            _gapBetweenPlatesMm = _editContext.Config.GapBetweenPlates;
            _placement          = _editContext.Placement;

            OnPropertyChanged(nameof(PlateThicknessMm));
            OnPropertyChanged(nameof(MarginVerticalMm));
            OnPropertyChanged(nameof(MarginHorizontalMm));
            OnPropertyChanged(nameof(GenerateBothMembers));
            OnPropertyChanged(nameof(GapBetweenPlatesMm));
            OnPropertyChanged(nameof(HasPlacement));
            OnPropertyChanged(nameof(IsTwoMemberMode));
            OnPropertyChanged(nameof(PrimaryMemberName));
            OnPropertyChanged(nameof(SecondaryMemberName));
            CalculatePreview();
        }

        // ── Seleção ────────────────────────────────────────────────────────

        public bool HasPlacement => _placement != null && _placement.Members.Count > 0;
        public bool IsTwoMemberMode => _placement?.Members.Count == 2;

        public string PrimaryMemberName =>
            _placement?.Members.Count > 0 ? _placement.Members[0].Name : "(nenhuma peça selecionada)";
        public string SecondaryMemberName =>
            _placement?.Members.Count > 1 ? _placement.Members[1].Name : null;

        private void OnMemberPicked(EndPlatePlacement placement)
        {
            _dispatcher.Invoke(() =>
            {
                _placement = placement;
                OnPropertyChanged(nameof(HasPlacement));
                OnPropertyChanged(nameof(IsTwoMemberMode));
                OnPropertyChanged(nameof(PrimaryMemberName));
                OnPropertyChanged(nameof(SecondaryMemberName));
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

        // ── Configuração ───────────────────────────────────────────────────

        public double PlateThicknessMm
        {
            get => _plateThicknessMm;
            set { if (Set(ref _plateThicknessMm, value)) CalculatePreview(); }
        }

        public double MarginVerticalMm
        {
            get => _marginVerticalMm;
            set { if (Set(ref _marginVerticalMm, value)) CalculatePreview(); }
        }

        public double MarginHorizontalMm
        {
            get => _marginHorizontalMm;
            set { if (Set(ref _marginHorizontalMm, value)) CalculatePreview(); }
        }

        /// <summary>Só relevante no Modo 2: true = as duas peças recebem chapa.</summary>
        public bool GenerateBothMembers
        {
            get => _generateBothMembers;
            set { if (Set(ref _generateBothMembers, value)) CalculatePreview(); }
        }

        /// <summary>Só relevante no Modo 2: afastamento entre as duas chapas (0 = encostadas).</summary>
        public double GapBetweenPlatesMm
        {
            get => _gapBetweenPlatesMm;
            set { if (Set(ref _gapBetweenPlatesMm, value)) CalculatePreview(); }
        }

        public ObservableCollection<string> MemberSummaries { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Warnings         { get; } = new ObservableCollection<string>();

        private void CalculatePreview()
        {
            MemberSummaries.Clear();
            Warnings.Clear();
            _definitionsValid = true;

            if (!HasPlacement) return;
            try
            {
                var config = BuildConfig();
                for (int i = 0; i < _placement.Members.Count; i++)
                {
                    var member = _placement.Members[i];
                    bool included = i == 0 || config.GenerateBothMembers;

                    var def = EndPlateCalculator.Calculate(member.HeightMm, member.WidthMm, config);
                    string status = included ? "" : "  (não será criada — 'ambas as peças' desligado)";
                    MemberSummaries.Add($"{member.Name}: {def.WidthMm:F0} × {def.HeightMm:F0} mm{status}");

                    foreach (var w in def.Warnings) Warnings.Add($"{member.Name}: {w}");
                    if (included && !def.IsValid) _definitionsValid = false;
                }
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                _definitionsValid = false;
                Warnings.Add($"Erro no cálculo: {ex.Message}");
            }
        }

        private EndPlateConfig BuildConfig() => new EndPlateConfig
        {
            PlateThickness      = PlateThicknessMm,
            MarginVertical      = MarginVerticalMm,
            MarginHorizontal    = MarginHorizontalMm,
            GenerateBothMembers = GenerateBothMembers,
            GapBetweenPlates    = GapBetweenPlatesMm
        };

        // ── Criação ────────────────────────────────────────────────────────

        private void CreateEndPlate()
        {
            CalculatePreview();
            if (!_definitionsValid || !(_placement?.IsValid ?? false)) return;

            _createHandler.Placement   = _placement;
            _createHandler.Config      = BuildConfig();
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
                        ? "Chapa de topo atualizada com sucesso."
                        : "Chapa de topo criada com sucesso.");
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Erro ao criar chapa de topo:\n\n{error}",
                        "SAGA Structural Tools",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            });
        }

        public bool CanClose => !_isSubmitting;

        public RelayCommand PickSingleCommand        { get; }
        public RelayCommand PickTwoCommand            { get; }
        public RelayCommand CalculatePreviewCommand  { get; }
        public RelayCommand CreateCommand            { get; }
    }
}
