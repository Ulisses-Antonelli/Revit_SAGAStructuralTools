using SAGAStructuralTools.BasePlate.Domain;
using SAGAStructuralTools.UI.Converters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class BasePlateViewModel : ViewModelBase
    {
        private readonly BasePlateCalculator _calculator = new BasePlateCalculator();
        private readonly BoltLayoutService _boltLayoutService = new BoltLayoutService();
        private bool _isUpdating;
        private bool _canCreateConnection;
        private string _overallStatusText;
        private Brush _overallStatusBackground;
        private Brush _overallStatusForeground;
        private string _anchorTensionResult;
        private string _anchorShearResult;
        private string _anchorConcreteShearResistance;
        private string _anchorConcreteTensionResistance;
        private string _anchorSteelShearResistance;
        private string _anchorSteelTensionResistance;
        private string _minimumPlateThickness;
        private string _minimumStiffenerThickness;
        private string _concreteUtilizationResult;
        private string _steelUtilization1Result;
        private string _steelUtilization2Result;
        private Brush _concreteUtilizationBackground;
        private Brush _steelUtilization1Background;
        private Brush _steelUtilization2Background;
        private double _sketchPlateLengthX;
        private double _sketchPlateLengthY;
        private double _sketchProfileDepth;
        private double _sketchProfileFlangeWidth;
        private double _sketchAnchorDiameter;
        private double _sketchAnchorEdgeDistanceX;
        private double _sketchAnchorEdgeDistanceY;
        private double _sketchPlateThickness;
        private double _sketchStiffenerHeight;
        private double _sketchStiffenerThickness;
        private string _depthMm;
        private string _flangeWidthMm;
        private string _webThicknessMm;
        private string _flangeThicknessMm;
        private string _compressionForceTf;
        private string _tensionForceTf;
        private string _momentX_TfM;
        private string _momentY_TfM;
        private string _shearX_Tf;
        private string _shearY_Tf;
        private string _anchorDiameterMm;
        private HeavyHexNutOption _selectedAnchorOption;
        private string _anchorLengthMm;
        private bool _hasHook;
        private string _corrosionAllowanceMm;
        private string _anchorFyMpa;
        private string _anchorFuMpa;
        private string _anchorsX;
        private string _anchorsY;
        private int _selectedAnchorsX;
        private int _selectedAnchorsY;
        private string _totalAnchors;
        private string _anchorEdgeDistanceXmm;
        private string _anchorEdgeDistanceYmm;
        private string _plateLengthXmm;
        private string _plateLengthYmm;
        private string _plateThicknessMm;
        private PlateThicknessOption _selectedPlateThicknessOption;
        private string _plateFyMpa;
        private string _plateFuMpa;
        private string _stiffenerThicknessMm;
        private PlateThicknessOption _selectedStiffenerThicknessOption;
        private string _stiffenerHeightMm;
        private bool _hasMiddleStiffener;
        private string _concreteFckMpa;
        private ConcreteStrengthOption _selectedConcreteStrengthOption;
        private string _concreteAreaRatioA2A1;
        private string _concreteEdgeDistanceXmm;
        private string _concreteEdgeDistanceYmm;

        public BasePlateViewModel()
        {
            CreateConnectionCommand = new RelayCommand(_ => { }, _ => CanCreateConnection);
            ResetDefaults();
            Recalculate();
        }

        public ObservableCollection<BasePlateVerificationItem> Verifications { get; } =
            new ObservableCollection<BasePlateVerificationItem>();
        public ObservableCollection<BoltPoint> BoltPoints { get; } =
            new ObservableCollection<BoltPoint>();
        public IReadOnlyList<HeavyHexNutOption> AnchorDiameterOptions =>
            HeavyHexNutCatalog.Options;
        public IReadOnlyList<int> AnchorCountOptions { get; } = new[] { 2, 3, 4 };
        public IReadOnlyList<PlateThicknessOption> PlateThicknessOptions =>
            PlateThicknessCatalog.Options;
        public IReadOnlyList<ConcreteStrengthOption> ConcreteStrengthOptions =>
            ConcreteStrengthCatalog.Options;

        public ICommand CreateConnectionCommand { get; }

        public string DepthMm { get => _depthMm; set => Set(ref _depthMm, value); }
        public string FlangeWidthMm { get => _flangeWidthMm; set => Set(ref _flangeWidthMm, value); }
        public string WebThicknessMm { get => _webThicknessMm; set => Set(ref _webThicknessMm, value); }
        public string FlangeThicknessMm { get => _flangeThicknessMm; set => Set(ref _flangeThicknessMm, value); }
        public string CompressionForceTf { get => _compressionForceTf; set => Set(ref _compressionForceTf, value); }
        public string TensionForceTf { get => _tensionForceTf; set => Set(ref _tensionForceTf, value); }
        public string MomentX_TfM { get => _momentX_TfM; set => Set(ref _momentX_TfM, value); }
        public string MomentY_TfM { get => _momentY_TfM; set => Set(ref _momentY_TfM, value); }
        public string ShearX_Tf { get => _shearX_Tf; set => Set(ref _shearX_Tf, value); }
        public string ShearY_Tf { get => _shearY_Tf; set => Set(ref _shearY_Tf, value); }
        public string AnchorDiameterMm { get => _anchorDiameterMm; set => Set(ref _anchorDiameterMm, value); }
        public HeavyHexNutOption SelectedAnchorOption
        {
            get => _selectedAnchorOption;
            set
            {
                if (!Set(ref _selectedAnchorOption, value) || value == null) return;
                AnchorDiameterMm = value.DiameterMm.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
            }
        }
        public string AnchorLengthMm { get => _anchorLengthMm; set => Set(ref _anchorLengthMm, value); }
        public bool HasHook { get => _hasHook; set => Set(ref _hasHook, value); }
        public string CorrosionAllowanceMm { get => _corrosionAllowanceMm; set => Set(ref _corrosionAllowanceMm, value); }
        public string AnchorFyMpa { get => _anchorFyMpa; set => Set(ref _anchorFyMpa, value); }
        public string AnchorFuMpa { get => _anchorFuMpa; set => Set(ref _anchorFuMpa, value); }
        public string AnchorsX { get => _anchorsX; set => Set(ref _anchorsX, value); }
        public string AnchorsY { get => _anchorsY; set => Set(ref _anchorsY, value); }
        public int SelectedAnchorsX
        {
            get => _selectedAnchorsX;
            set
            {
                if (!Set(ref _selectedAnchorsX, value)) return;
                AnchorsX = value.ToString(CultureInfo.InvariantCulture);
            }
        }
        public int SelectedAnchorsY
        {
            get => _selectedAnchorsY;
            set
            {
                if (!Set(ref _selectedAnchorsY, value)) return;
                AnchorsY = value.ToString(CultureInfo.InvariantCulture);
            }
        }
        public string TotalAnchors { get => _totalAnchors; private set => Set(ref _totalAnchors, value); }
        public string AnchorEdgeDistanceXmm { get => _anchorEdgeDistanceXmm; set => Set(ref _anchorEdgeDistanceXmm, value); }
        public string AnchorEdgeDistanceYmm { get => _anchorEdgeDistanceYmm; set => Set(ref _anchorEdgeDistanceYmm, value); }
        public string PlateLengthXmm { get => _plateLengthXmm; set => Set(ref _plateLengthXmm, value); }
        public string PlateLengthYmm { get => _plateLengthYmm; set => Set(ref _plateLengthYmm, value); }
        public string PlateThicknessMm { get => _plateThicknessMm; set => Set(ref _plateThicknessMm, value); }
        public PlateThicknessOption SelectedPlateThicknessOption
        {
            get => _selectedPlateThicknessOption;
            set
            {
                if (!Set(ref _selectedPlateThicknessOption, value) || value == null) return;
                PlateThicknessMm = value.ThicknessMm.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
            }
        }
        public string PlateFyMpa { get => _plateFyMpa; set => Set(ref _plateFyMpa, value); }
        public string PlateFuMpa { get => _plateFuMpa; set => Set(ref _plateFuMpa, value); }
        public string StiffenerThicknessMm { get => _stiffenerThicknessMm; set => Set(ref _stiffenerThicknessMm, value); }
        public PlateThicknessOption SelectedStiffenerThicknessOption
        {
            get => _selectedStiffenerThicknessOption;
            set
            {
                if (!Set(ref _selectedStiffenerThicknessOption, value) || value == null) return;
                StiffenerThicknessMm = value.ThicknessMm.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
            }
        }
        public string StiffenerHeightMm { get => _stiffenerHeightMm; set => Set(ref _stiffenerHeightMm, value); }
        public bool HasMiddleStiffener { get => _hasMiddleStiffener; set => Set(ref _hasMiddleStiffener, value); }
        public string ConcreteFckMpa { get => _concreteFckMpa; set => Set(ref _concreteFckMpa, value); }
        public ConcreteStrengthOption SelectedConcreteStrengthOption
        {
            get => _selectedConcreteStrengthOption;
            set
            {
                if (!Set(ref _selectedConcreteStrengthOption, value) || value == null) return;
                ConcreteFckMpa = value.FckMpa.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
            }
        }
        public string ConcreteAreaRatioA2A1 { get => _concreteAreaRatioA2A1; set => Set(ref _concreteAreaRatioA2A1, value); }
        public string ConcreteEdgeDistanceXmm { get => _concreteEdgeDistanceXmm; set => Set(ref _concreteEdgeDistanceXmm, value); }
        public string ConcreteEdgeDistanceYmm { get => _concreteEdgeDistanceYmm; set => Set(ref _concreteEdgeDistanceYmm, value); }
        public bool CanCreateConnection { get => _canCreateConnection; private set => Set(ref _canCreateConnection, value); }
        public string OverallStatusText { get => _overallStatusText; private set => Set(ref _overallStatusText, value); }
        public Brush OverallStatusBackground { get => _overallStatusBackground; private set => Set(ref _overallStatusBackground, value); }
        public Brush OverallStatusForeground { get => _overallStatusForeground; private set => Set(ref _overallStatusForeground, value); }
        public string AnchorTensionResult { get => _anchorTensionResult; private set => Set(ref _anchorTensionResult, value); }
        public string AnchorShearResult { get => _anchorShearResult; private set => Set(ref _anchorShearResult, value); }
        public string AnchorConcreteShearResistance { get => _anchorConcreteShearResistance; private set => Set(ref _anchorConcreteShearResistance, value); }
        public string AnchorConcreteTensionResistance { get => _anchorConcreteTensionResistance; private set => Set(ref _anchorConcreteTensionResistance, value); }
        public string AnchorSteelShearResistance { get => _anchorSteelShearResistance; private set => Set(ref _anchorSteelShearResistance, value); }
        public string AnchorSteelTensionResistance { get => _anchorSteelTensionResistance; private set => Set(ref _anchorSteelTensionResistance, value); }
        public string MinimumPlateThickness { get => _minimumPlateThickness; private set => Set(ref _minimumPlateThickness, value); }
        public string MinimumStiffenerThickness { get => _minimumStiffenerThickness; private set => Set(ref _minimumStiffenerThickness, value); }
        public string ConcreteUtilizationResult { get => _concreteUtilizationResult; private set => Set(ref _concreteUtilizationResult, value); }
        public string SteelUtilization1Result { get => _steelUtilization1Result; private set => Set(ref _steelUtilization1Result, value); }
        public string SteelUtilization2Result { get => _steelUtilization2Result; private set => Set(ref _steelUtilization2Result, value); }
        public Brush ConcreteUtilizationBackground { get => _concreteUtilizationBackground; private set => Set(ref _concreteUtilizationBackground, value); }
        public Brush SteelUtilization1Background { get => _steelUtilization1Background; private set => Set(ref _steelUtilization1Background, value); }
        public Brush SteelUtilization2Background { get => _steelUtilization2Background; private set => Set(ref _steelUtilization2Background, value); }
        public double SketchPlateLengthX { get => _sketchPlateLengthX; private set => Set(ref _sketchPlateLengthX, value); }
        public double SketchPlateLengthY { get => _sketchPlateLengthY; private set => Set(ref _sketchPlateLengthY, value); }
        public double SketchProfileDepth { get => _sketchProfileDepth; private set => Set(ref _sketchProfileDepth, value); }
        public double SketchProfileFlangeWidth { get => _sketchProfileFlangeWidth; private set => Set(ref _sketchProfileFlangeWidth, value); }
        public double SketchAnchorDiameter { get => _sketchAnchorDiameter; private set => Set(ref _sketchAnchorDiameter, value); }
        public double SketchAnchorEdgeDistanceX { get => _sketchAnchorEdgeDistanceX; private set => Set(ref _sketchAnchorEdgeDistanceX, value); }
        public double SketchAnchorEdgeDistanceY { get => _sketchAnchorEdgeDistanceY; private set => Set(ref _sketchAnchorEdgeDistanceY, value); }
        public double SketchPlateThickness { get => _sketchPlateThickness; private set => Set(ref _sketchPlateThickness, value); }
        public double SketchStiffenerHeight { get => _sketchStiffenerHeight; private set => Set(ref _sketchStiffenerHeight, value); }
        public double SketchStiffenerThickness { get => _sketchStiffenerThickness; private set => Set(ref _sketchStiffenerThickness, value); }

        protected override void OnPropertyChanged(string name = null)
        {
            base.OnPropertyChanged(name);
            if (_isUpdating || string.IsNullOrWhiteSpace(name) || IsOutputProperty(name))
                return;
            Recalculate();
        }

        private void ResetDefaults()
        {
            _isUpdating = true;
            DepthMm = "300";
            FlangeWidthMm = "150";
            WebThicknessMm = "6,3";
            FlangeThicknessMm = "9,5";
            CompressionForceTf = "20";
            TensionForceTf = "0";
            MomentX_TfM = "1,5";
            MomentY_TfM = "0,8";
            ShearX_Tf = "2";
            ShearY_Tf = "1";
            SelectedAnchorOption = HeavyHexNutCatalog.FindByDiameter(19.05);
            AnchorLengthMm = "250";
            HasHook = true;
            CorrosionAllowanceMm = "0";
            AnchorFyMpa = "250";
            AnchorFuMpa = "400";
            SelectedAnchorsX = 2;
            SelectedAnchorsY = 2;
            TotalAnchors = "4";
            AnchorEdgeDistanceXmm = "60";
            AnchorEdgeDistanceYmm = "70";
            PlateLengthXmm = "300";
            PlateLengthYmm = "450";
            SelectedPlateThicknessOption = PlateThicknessCatalog.FindByThickness(19.00);
            PlateFyMpa = "250";
            PlateFuMpa = "400";
            SelectedStiffenerThicknessOption = PlateThicknessCatalog.FindByThickness(6.35);
            StiffenerHeightMm = "0";
            SelectedConcreteStrengthOption = ConcreteStrengthCatalog.FindByFck(30.0);
            ConcreteAreaRatioA2A1 = "1";
            ConcreteEdgeDistanceXmm = "300";
            ConcreteEdgeDistanceYmm = "300";
            _isUpdating = false;
        }

        private void Recalculate()
        {
            _isUpdating = true;
            Verifications.Clear();
            try
            {
                UpdateTotalAnchors();
                BasePlateInput input = ReadInput();
                BasePlateCalculationResult result = _calculator.Calculate(input);
                AnchorTensionResult = Format(result.AnchorTensionTf);
                AnchorShearResult = Format(result.AnchorShearTf);
                AnchorConcreteShearResistance = Format(result.AnchorConcreteShearResistanceTf);
                AnchorConcreteTensionResistance = Format(result.AnchorConcreteTensionResistanceTf);
                AnchorSteelShearResistance = Format(result.AnchorSteelShearResistanceTf);
                AnchorSteelTensionResistance = Format(result.AnchorSteelTensionResistanceTf);
                MinimumPlateThickness = Format(result.MinimumPlateThicknessMm);
                MinimumStiffenerThickness = Format(result.MinimumStiffenerThicknessMm);
                ConcreteUtilizationResult = FormatUtilization(result.AnchorConcreteUtilization);
                SteelUtilization1Result = FormatUtilization(result.AnchorSteelUtilization1);
                SteelUtilization2Result = FormatUtilization(result.AnchorSteelUtilization2);
                ConcreteUtilizationBackground = GetUtilizationBackground(result.AnchorConcreteUtilization);
                SteelUtilization1Background = GetUtilizationBackground(result.AnchorSteelUtilization1);
                SteelUtilization2Background = GetUtilizationBackground(result.AnchorSteelUtilization2);
                UpdateSketch(input);
                AddRows(input, result);
                CanCreateConnection = Verifications.All(v => v.IsOk);
                SetOverallStatus(CanCreateConnection);
            }
            catch (Exception ex)
            {
                SagaLog.Exception("BasePlateViewModel.Recalculate", ex);
                AnchorTensionResult = "";
                AnchorShearResult = "";
                AnchorConcreteShearResistance = "";
                AnchorConcreteTensionResistance = "";
                AnchorSteelShearResistance = "";
                AnchorSteelTensionResistance = "";
                MinimumPlateThickness = "";
                MinimumStiffenerThickness = "";
                ConcreteUtilizationResult = "";
                SteelUtilization1Result = "";
                SteelUtilization2Result = "";
                ConcreteUtilizationBackground = GetUtilizationBackground(double.PositiveInfinity);
                SteelUtilization1Background = GetUtilizationBackground(double.PositiveInfinity);
                SteelUtilization2Background = GetUtilizationBackground(double.PositiveInfinity);
                BoltPoints.Clear();
                Verifications.Add(BasePlateVerificationItem.Create("Entrada", false, ex.Message));
                CanCreateConnection = false;
                SetOverallStatus(false);
            }
            finally
            {
                _isUpdating = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void AddRows(BasePlateInput input, BasePlateCalculationResult result)
        {
            AddFromResult(result, "lx > bf", "Aumentar dimensão lx da placa de base.");
            AddFromResult(result, "ly > d", "Aumentar dimensão ly da placa de base.");
            AddComputed("nbx/nby", input.AnchorsX >= 2 && input.AnchorsY >= 2, "Informe ao menos 2 chumbadores em cada direção.");
            AddFromResult(result, "Chumbador-borda", "Aumentar distância entre o chumbador e a borda da placa.");
            AddComputed("Chumbador-nervura", true, "Aumentar distância entre o chumbador e a nervura.");
            if (input.HasMiddleStiffener)
                AddComputed("Nervura média", true, "Aumentar a distância entre os chumbadores e a nervura média.");
            AddFromResult(result, "Chumbador-chumbador", "Aumentar distância entre chumbadores.");
            AddComputed("tpl", input.PlateThicknessMm >= result.MinimumPlateThicknessMm, "Aumentar espessura da placa de base.", result.MinimumPlateThicknessMm > 0 ? input.PlateThicknessMm / result.MinimumPlateThicknessMm : 0);
            AddComputed("tn", input.StiffenerHeightMm <= 0 || input.StiffenerThicknessMm >= result.MinimumStiffenerThicknessMm, "Aumentar espessura das nervuras.", result.MinimumStiffenerThicknessMm > 0 ? input.StiffenerThicknessMm / result.MinimumStiffenerThicknessMm : 0);
            AddComputed("Pressão concreto", result.ConcretePressureTfM2 <= result.ConcreteResistanceTfM2, "Pressão elevada no concreto. Aumentar as dimensões da placa de base ou revisar o concreto.", result.ConcreteResistanceTfM2 > 0 ? result.ConcretePressureTfM2 / result.ConcreteResistanceTfM2 : 0, "Compressão");
            AddFromResult(result, "Concreto-chumbador", "Falha na ancoragem no concreto. Reavaliar os chumbadores, o embutimento ou o concreto.", "Concreto-chumbador", result.AnchorConcreteUtilization, GetConcreteGoverningCase(result));
            AddFromResult(result, "Aco", "Falha no chumbador. Aumentar o diâmetro, a resistência ou a quantidade de chumbadores.", "Aço", Math.Max(result.AnchorSteelUtilization1, result.AnchorSteelUtilization2), result.GoverningSteelMechanism);
        }

        private void AddFromResult(BasePlateCalculationResult result, string verificationName, string errorMessage)
        {
            AddFromResult(result, verificationName, errorMessage, verificationName);
        }

        private void AddFromResult(
            BasePlateCalculationResult result,
            string verificationName,
            string errorMessage,
            string displayName)
        {
            AddFromResult(result, verificationName, errorMessage, displayName, 0);
        }

        private void AddFromResult(
            BasePlateCalculationResult result,
            string verificationName,
            string errorMessage,
            string displayName,
            double utilization)
        {
            AddFromResult(result, verificationName, errorMessage, displayName, utilization, null);
        }

        private void AddFromResult(
            BasePlateCalculationResult result,
            string verificationName,
            string errorMessage,
            string displayName,
            double utilization,
            string governingCase)
        {
            VerificationResult verification = result.Verifications.FirstOrDefault(v => v.Name == verificationName);
            if (verification == null)
            {
                AddComputed(displayName, true, errorMessage);
                return;
            }
            AddComputed(displayName, verification.Status == VerificationStatus.Passed, errorMessage, utilization, governingCase);
        }

        private void AddComputed(
            string name,
            bool isOk,
            string errorMessage,
            double utilization = 0,
            string governingCase = null)
        {
            Verifications.Add(BasePlateVerificationItem.Create(
                name,
                isOk,
                errorMessage,
                utilization > 0 ? (double?)utilization : null,
                governingCase));
        }

        private BasePlateInput ReadInput()
        {
            int anchorsX = ReadInt(AnchorsX, "nbx");
            int anchorsY = ReadInt(AnchorsY, "nby");
            int totalAnchors = BoltLayoutService.CalculateTotalAnchors(anchorsX, anchorsY);
            TotalAnchors = totalAnchors.ToString(CultureInfo.GetCultureInfo("pt-BR"));
            return new BasePlateInput
            {
                DepthMm = ReadDouble(DepthMm, "d"),
                FlangeWidthMm = ReadDouble(FlangeWidthMm, "bf"),
                WebThicknessMm = ReadDouble(WebThicknessMm, "tw"),
                FlangeThicknessMm = ReadDouble(FlangeThicknessMm, "tf"),
                CompressionForceTf = ReadDouble(CompressionForceTf, "Nc"),
                TensionForceTf = ReadDouble(TensionForceTf, "Nt"),
                MomentX_TfM = ReadDouble(MomentX_TfM, "Mx"),
                MomentY_TfM = ReadDouble(MomentY_TfM, "My"),
                ShearX_Tf = ReadDouble(ShearX_Tf, "Vx"),
                ShearY_Tf = ReadDouble(ShearY_Tf, "Vy"),
                PlateFyMpa = ReadDouble(PlateFyMpa, "fy,pl"),
                PlateFuMpa = ReadDouble(PlateFuMpa, "fu,pl"),
                ConcreteFckMpa = ReadDouble(ConcreteFckMpa, "fck"),
                ConcreteAreaRatioA2A1 = ReadDouble(ConcreteAreaRatioA2A1, "A2/A1"),
                ConcreteEdgeDistanceXmm = ReadDouble(ConcreteEdgeDistanceXmm, "cx"),
                ConcreteEdgeDistanceYmm = ReadDouble(ConcreteEdgeDistanceYmm, "cy"),
                AnchorFyMpa = ReadDouble(AnchorFyMpa, "fy,b"),
                AnchorFuMpa = ReadDouble(AnchorFuMpa, "fu,b"),
                ColumnFyMpa = ReadDouble(PlateFyMpa, "fy,pl"),
                ColumnFuMpa = ReadDouble(PlateFuMpa, "fu,pl"),
                PlateLengthXmm = ReadDouble(PlateLengthXmm, "lx"),
                PlateLengthYmm = ReadDouble(PlateLengthYmm, "ly"),
                PlateThicknessMm = ReadDouble(PlateThicknessMm, "tpl"),
                AnchorDiameterMm = ReadDouble(AnchorDiameterMm, "db"),
                AnchorLengthMm = ReadDouble(AnchorLengthMm, "lb"),
                EmbedmentLengthMm = ReadDouble(AnchorLengthMm, "lb"),
                HasHook = HasHook,
                CorrosionAllowanceMm = ReadDouble(CorrosionAllowanceMm, "ecorrosão"),
                TotalAnchors = totalAnchors,
                AnchorsX = anchorsX,
                AnchorsY = anchorsY,
                AnchorEdgeDistanceXmm = ReadDouble(AnchorEdgeDistanceXmm, "a1"),
                AnchorEdgeDistanceYmm = ReadDouble(AnchorEdgeDistanceYmm, "b1"),
                StiffenerThicknessMm = ReadDouble(StiffenerThicknessMm, "tn"),
                StiffenerHeightMm = ReadDouble(StiffenerHeightMm, "hn"),
                HasMiddleStiffener = HasMiddleStiffener
            };
        }

        private void UpdateTotalAnchors()
        {
            if (int.TryParse(AnchorsX, NumberStyles.Integer, CultureInfo.GetCultureInfo("pt-BR"), out int anchorsX) &&
                int.TryParse(AnchorsY, NumberStyles.Integer, CultureInfo.GetCultureInfo("pt-BR"), out int anchorsY))
                TotalAnchors = BoltLayoutService.CalculateTotalAnchors(anchorsX, anchorsY)
                    .ToString(CultureInfo.GetCultureInfo("pt-BR"));
            else
                TotalAnchors = "";
        }

        private void UpdateSketch(BasePlateInput input)
        {
            SketchPlateLengthX = input.PlateLengthXmm;
            SketchPlateLengthY = input.PlateLengthYmm;
            SketchProfileDepth = input.DepthMm;
            SketchProfileFlangeWidth = input.FlangeWidthMm;
            SketchAnchorDiameter = input.AnchorDiameterMm;
            SketchAnchorEdgeDistanceX = input.AnchorEdgeDistanceXmm;
            SketchAnchorEdgeDistanceY = input.AnchorEdgeDistanceYmm;
            SketchPlateThickness = input.PlateThicknessMm;
            SketchStiffenerHeight = input.StiffenerHeightMm;
            SketchStiffenerThickness = input.StiffenerThicknessMm;

            BoltPoints.Clear();
            if (input.AnchorsX < 2 || input.AnchorsY < 2) return;
            if (input.PlateLengthXmm <= 2 * input.AnchorEdgeDistanceXmm) return;
            if (input.PlateLengthYmm <= 2 * input.AnchorEdgeDistanceYmm) return;

            foreach (BoltPoint point in _boltLayoutService.GeneratePerimeterLayout(
                input.PlateLengthXmm,
                input.PlateLengthYmm,
                input.AnchorEdgeDistanceXmm,
                input.AnchorEdgeDistanceYmm,
                input.AnchorsX,
                input.AnchorsY))
            {
                BoltPoints.Add(point);
            }
        }

        private static double ReadDouble(string text, string fieldName)
        {
            if (FlexibleDoubleConverter.TryParse(text, out double value))
                return value;
            throw new InvalidOperationException($"{fieldName}: valor inválido.");
        }

        private static int ReadInt(string text, string fieldName)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.GetCultureInfo("pt-BR"), out int value))
                return value;
            if (fieldName == "nbx" || fieldName == "nby")
                throw new InvalidOperationException("Informe ao menos 2 chumbadores em cada direção.");
            throw new InvalidOperationException($"{fieldName}: número inteiro inválido.");
        }

        private void SetOverallStatus(bool isOk)
        {
            OverallStatusText = isOk
                ? "✓ Todas as verificações foram atendidas. A ligação está apta para criação."
                : "✕ Existem verificações pendentes. Corrija os itens destacados em vermelho antes de criar a ligação.";
            OverallStatusBackground = isOk
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                : new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
            OverallStatusForeground = new SolidColorBrush(Colors.White);
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"));
        }

        private static string FormatUtilization(double value)
        {
            return string.Format(
                CultureInfo.GetCultureInfo("pt-BR"),
                "{0:0.00} | {1:0.0} %",
                value,
                value * 100.0);
        }

        private static Brush GetUtilizationBackground(double value)
        {
            return value <= 1.0
                ? new SolidColorBrush(Color.FromRgb(0x92, 0xD0, 0x50))
                : new SolidColorBrush(Color.FromRgb(0xF4, 0xCC, 0xCC));
        }

        private static string GetConcreteGoverningCase(BasePlateCalculationResult result)
        {
            if (result.AnchorConcreteUtilization <= 0) return null;

            double tensionRatio = result.AnchorConcreteTensionResistanceTf > 0
                ? result.AnchorTensionTf / result.AnchorConcreteTensionResistanceTf
                : double.PositiveInfinity;
            double shearRatio = result.AnchorConcreteShearResistanceTf > 0
                ? result.AnchorShearTf / result.AnchorConcreteShearResistanceTf
                : double.PositiveInfinity;

            return tensionRatio >= shearRatio
                ? result.GoverningConcreteTensionMechanism
                : result.GoverningConcreteShearMechanism;
        }

        private static bool IsOutputProperty(string name)
        {
            return name == nameof(CanCreateConnection) ||
                name == nameof(OverallStatusText) ||
                name == nameof(OverallStatusBackground) ||
                name == nameof(OverallStatusForeground) ||
                name == nameof(AnchorTensionResult) ||
                name == nameof(AnchorShearResult) ||
                name == nameof(AnchorConcreteShearResistance) ||
                name == nameof(AnchorConcreteTensionResistance) ||
                name == nameof(AnchorSteelShearResistance) ||
                name == nameof(AnchorSteelTensionResistance) ||
                name == nameof(MinimumPlateThickness) ||
                name == nameof(MinimumStiffenerThickness) ||
                name == nameof(ConcreteUtilizationResult) ||
                name == nameof(SteelUtilization1Result) ||
                name == nameof(SteelUtilization2Result) ||
                name == nameof(ConcreteUtilizationBackground) ||
                name == nameof(SteelUtilization1Background) ||
                name == nameof(SteelUtilization2Background) ||
                name == nameof(SketchPlateLengthX) ||
                name == nameof(SketchPlateLengthY) ||
                name == nameof(SketchProfileDepth) ||
                name == nameof(SketchProfileFlangeWidth) ||
                name == nameof(SketchAnchorDiameter) ||
                name == nameof(SketchAnchorEdgeDistanceX) ||
                name == nameof(SketchAnchorEdgeDistanceY) ||
                name == nameof(SketchPlateThickness) ||
                name == nameof(SketchStiffenerHeight) ||
                name == nameof(SketchStiffenerThickness) ||
                name == nameof(TotalAnchors);
        }
    }
}

