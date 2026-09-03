using SAGAStructuralTools.BasePlate.Domain;
using SAGAStructuralTools.UI.Converters;
using System;
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
        private bool _isUpdating;
        private bool _canCreateConnection;
        private string _overallStatusText;
        private Brush _overallStatusBackground;
        private Brush _overallStatusForeground;
        private string _anchorTensionResult;
        private string _anchorShearResult;
        private string _minimumPlateThickness;
        private string _minimumStiffenerThickness;
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
        private string _anchorLengthMm;
        private bool _hasHook;
        private string _corrosionAllowanceMm;
        private string _anchorFyMpa;
        private string _anchorFuMpa;
        private string _anchorsX;
        private string _anchorsY;
        private string _totalAnchors;
        private string _anchorEdgeDistanceXmm;
        private string _anchorEdgeDistanceYmm;
        private string _plateLengthXmm;
        private string _plateLengthYmm;
        private string _plateThicknessMm;
        private string _plateFyMpa;
        private string _plateFuMpa;
        private string _stiffenerThicknessMm;
        private string _stiffenerHeightMm;
        private bool _hasMiddleStiffener;
        private string _concreteFckMpa;
        private string _concreteAreaRatioA2A1;

        public BasePlateViewModel()
        {
            CreateConnectionCommand = new RelayCommand(_ => { }, _ => CanCreateConnection);
            ResetDefaults();
            Recalculate();
        }

        public ObservableCollection<BasePlateVerificationItem> Verifications { get; } =
            new ObservableCollection<BasePlateVerificationItem>();

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
        public string AnchorLengthMm { get => _anchorLengthMm; set => Set(ref _anchorLengthMm, value); }
        public bool HasHook { get => _hasHook; set => Set(ref _hasHook, value); }
        public string CorrosionAllowanceMm { get => _corrosionAllowanceMm; set => Set(ref _corrosionAllowanceMm, value); }
        public string AnchorFyMpa { get => _anchorFyMpa; set => Set(ref _anchorFyMpa, value); }
        public string AnchorFuMpa { get => _anchorFuMpa; set => Set(ref _anchorFuMpa, value); }
        public string AnchorsX { get => _anchorsX; set => Set(ref _anchorsX, value); }
        public string AnchorsY { get => _anchorsY; set => Set(ref _anchorsY, value); }
        public string TotalAnchors { get => _totalAnchors; private set => Set(ref _totalAnchors, value); }
        public string AnchorEdgeDistanceXmm { get => _anchorEdgeDistanceXmm; set => Set(ref _anchorEdgeDistanceXmm, value); }
        public string AnchorEdgeDistanceYmm { get => _anchorEdgeDistanceYmm; set => Set(ref _anchorEdgeDistanceYmm, value); }
        public string PlateLengthXmm { get => _plateLengthXmm; set => Set(ref _plateLengthXmm, value); }
        public string PlateLengthYmm { get => _plateLengthYmm; set => Set(ref _plateLengthYmm, value); }
        public string PlateThicknessMm { get => _plateThicknessMm; set => Set(ref _plateThicknessMm, value); }
        public string PlateFyMpa { get => _plateFyMpa; set => Set(ref _plateFyMpa, value); }
        public string PlateFuMpa { get => _plateFuMpa; set => Set(ref _plateFuMpa, value); }
        public string StiffenerThicknessMm { get => _stiffenerThicknessMm; set => Set(ref _stiffenerThicknessMm, value); }
        public string StiffenerHeightMm { get => _stiffenerHeightMm; set => Set(ref _stiffenerHeightMm, value); }
        public bool HasMiddleStiffener { get => _hasMiddleStiffener; set => Set(ref _hasMiddleStiffener, value); }
        public string ConcreteFckMpa { get => _concreteFckMpa; set => Set(ref _concreteFckMpa, value); }
        public string ConcreteAreaRatioA2A1 { get => _concreteAreaRatioA2A1; set => Set(ref _concreteAreaRatioA2A1, value); }
        public bool CanCreateConnection { get => _canCreateConnection; private set => Set(ref _canCreateConnection, value); }
        public string OverallStatusText { get => _overallStatusText; private set => Set(ref _overallStatusText, value); }
        public Brush OverallStatusBackground { get => _overallStatusBackground; private set => Set(ref _overallStatusBackground, value); }
        public Brush OverallStatusForeground { get => _overallStatusForeground; private set => Set(ref _overallStatusForeground, value); }
        public string AnchorTensionResult { get => _anchorTensionResult; private set => Set(ref _anchorTensionResult, value); }
        public string AnchorShearResult { get => _anchorShearResult; private set => Set(ref _anchorShearResult, value); }
        public string MinimumPlateThickness { get => _minimumPlateThickness; private set => Set(ref _minimumPlateThickness, value); }
        public string MinimumStiffenerThickness { get => _minimumStiffenerThickness; private set => Set(ref _minimumStiffenerThickness, value); }

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
            AnchorDiameterMm = "19";
            AnchorLengthMm = "250";
            HasHook = true;
            CorrosionAllowanceMm = "0";
            AnchorFyMpa = "250";
            AnchorFuMpa = "400";
            AnchorsX = "2";
            AnchorsY = "2";
            TotalAnchors = "4";
            AnchorEdgeDistanceXmm = "60";
            AnchorEdgeDistanceYmm = "70";
            PlateLengthXmm = "300";
            PlateLengthYmm = "450";
            PlateThicknessMm = "19";
            PlateFyMpa = "250";
            PlateFuMpa = "400";
            StiffenerThicknessMm = "0";
            StiffenerHeightMm = "0";
            ConcreteFckMpa = "30";
            ConcreteAreaRatioA2A1 = "1";
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
                MinimumPlateThickness = Format(result.MinimumPlateThicknessMm);
                MinimumStiffenerThickness = Format(result.MinimumStiffenerThicknessMm);
                AddRows(input, result);
                CanCreateConnection = Verifications.All(v => v.IsOk);
                SetOverallStatus(CanCreateConnection);
            }
            catch (Exception ex)
            {
                SagaLog.Exception("BasePlateViewModel.Recalculate", ex);
                AnchorTensionResult = "";
                AnchorShearResult = "";
                MinimumPlateThickness = "";
                MinimumStiffenerThickness = "";
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
            AddComputed("Concreto-chumbador", true, "Falha na ancoragem no concreto. Reavaliar os chumbadores, o embutimento ou o concreto.");
            AddComputed("Aço", true, "Falha no chumbador. Aumentar o diâmetro, a resistência ou a quantidade de chumbadores.");
        }

        private void AddFromResult(BasePlateCalculationResult result, string verificationName, string errorMessage)
        {
            VerificationResult verification = result.Verifications.FirstOrDefault(v => v.Name == verificationName);
            if (verification == null)
            {
                AddComputed(verificationName, true, errorMessage);
                return;
            }
            AddComputed(verificationName, verification.Status != VerificationStatus.Failed, errorMessage);
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
            int totalAnchors = Math.Max(0, anchorsX + anchorsY);
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
                TotalAnchors = (anchorsX + anchorsY).ToString(CultureInfo.GetCultureInfo("pt-BR"));
            else
                TotalAnchors = "";
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

        private static bool IsOutputProperty(string name)
        {
            return name == nameof(CanCreateConnection) ||
                name == nameof(OverallStatusText) ||
                name == nameof(OverallStatusBackground) ||
                name == nameof(OverallStatusForeground) ||
                name == nameof(AnchorTensionResult) ||
                name == nameof(AnchorShearResult) ||
                name == nameof(MinimumPlateThickness) ||
                name == nameof(MinimumStiffenerThickness) ||
                name == nameof(TotalAnchors);
        }
    }
}
