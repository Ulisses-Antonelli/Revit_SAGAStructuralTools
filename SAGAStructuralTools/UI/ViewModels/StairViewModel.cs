using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Stair;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Threading;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class StairViewModel : ViewModelBase
    {
        private readonly UIApplication      _uiApp;
        private readonly Dispatcher         _dispatcher;
        private readonly ExternalEvent      _pickEvent;
        private readonly BeamPickHandler    _pickHandler;
        private readonly ExternalEvent      _createEvent;
        private readonly StairCreationHandler _createHandler;

        // Estado de seleção
        private int       _pickTarget;
        private ElementId _lowerBeamId;
        private ElementId _upperBeamId;
        private XYZ       _lowerClickPoint;   // ponto do clique projetado no eixo da viga inferior
        private XYZ       _upperClickPoint;   // ponto do clique projetado no eixo da viga superior
        private string    _lowerBeamName = "Não selecionada";
        private string    _upperBeamName = "Não selecionada";

        // Configuração
        private double _width           = StairDefaults.Width;
        private double _treadDepth      = StairDefaults.TreadDepth;
        private double _treadThickness              = StairDefaults.TreadThickness;
        private double _intermediateLandingLength   = StairDefaults.IntermediateLandingLength;
        private bool   _applyBlondel                = StairDefaults.ApplyBlondel;
        private bool   _centerStair                 = StairDefaults.CenterStair;
        private bool   _includeTreads               = StairDefaults.IncludeTreads;
        private bool   _hasIntermediateLanding      = StairDefaults.HasIntermediateLanding;
        private string _stringerPath;
        private string _stringerType;
        private bool   _useAxis          = false;
        private bool   _isChannelProfile = false;

        // Preview
        private StairDefinition _preview;
        private bool _isCalculated;

        public StairViewModel(UIApplication uiApp)
        {
            SagaLog.Write("StairViewModel — construtor início");
            _uiApp      = uiApp;
            _dispatcher = Dispatcher.CurrentDispatcher;

            SagaLog.Write("StairViewModel — criando BeamPickHandler...");
            _pickHandler = new BeamPickHandler();
            _pickHandler.BeamPicked += OnBeamPicked;
            SagaLog.Write("StairViewModel — ExternalEvent.Create(pick)...");
            _pickEvent = ExternalEvent.Create(_pickHandler);

            SagaLog.Write("StairViewModel — criando StairCreationHandler...");
            _createHandler = new StairCreationHandler();
            _createHandler.Completed += OnCreationCompleted;
            SagaLog.Write("StairViewModel — ExternalEvent.Create(create)...");
            _createEvent = ExternalEvent.Create(_createHandler);

            SagaLog.Write("StairViewModel — criando comandos...");
            SelectLowerBeamCommand   = new RelayCommand(_ => StartPick(1));
            SelectUpperBeamCommand   = new RelayCommand(_ => StartPick(2));
            BrowseStringerCommand    = new RelayCommand(_ => BrowseStringer());
            CalculatePreviewCommand  = new RelayCommand(_ => CalculatePreview(), _ => CanCalculate());
            CreateStairCommand       = new RelayCommand(_ => CreateStair(),      _ => CanCreate());
            SagaLog.Write("StairViewModel — construtor OK");
        }

        // ── Seleção de vigas ────────────────────────────────────────────────

        public string LowerBeamName    { get => _lowerBeamName; set => Set(ref _lowerBeamName, value); }
        public string UpperBeamName    { get => _upperBeamName; set => Set(ref _upperBeamName, value); }

        // Borda do input fica verde quando selecionado (DataTrigger no XAML).
        public bool LowerBeamSelected  => _lowerBeamId != null;
        public bool UpperBeamSelected  => _upperBeamId != null;

        private void StartPick(int target)
        {
            _pickTarget = target;
            _pickEvent.Raise();
        }

        private void OnBeamPicked(ElementId id, string name, XYZ axisPoint)
        {
            _dispatcher.Invoke(() =>
            {
                if (_pickTarget == 1)
                {
                    _lowerBeamId     = id;
                    _lowerClickPoint = axisPoint;
                    LowerBeamName    = name;
                    OnPropertyChanged(nameof(LowerBeamSelected));
                }
                else
                {
                    // Valida se a "viga superior" está realmente acima da inferior.
                    if (_lowerClickPoint != null && axisPoint.Z < _lowerClickPoint.Z - 1e-4)
                    {
                        System.Windows.MessageBox.Show(
                            "A viga selecionada como \"Viga superior\" está abaixo da viga inferior.\n\n" +
                            "Por favor, selecione a viga do nível superior.",
                            "Seleção incorreta",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                        return;
                    }

                    _upperBeamId     = id;
                    UpperBeamName    = name;

                    // Alinha o ponto de conexão superior para que a escada
                    // corra em linha reta em planta, independente de onde o usuário clicar.
                    // Projeta o clique da viga inferior (na cota da viga superior)
                    // sobre o eixo da viga superior.
                    _upperClickPoint = AlignToUpperBeam(id, axisPoint);
                    OnPropertyChanged(nameof(UpperBeamSelected));
                }
            });
        }

        /// <summary>
        /// Garante que o ponto superior está "diretamente à frente" do ponto inferior,
        /// evitando que a escada fique diagonal em planta.
        /// </summary>
        private XYZ AlignToUpperBeam(ElementId upperBeamId, XYZ fallback)
        {
            if (_lowerClickPoint == null) return fallback;

            try
            {
                var doc       = _uiApp.ActiveUIDocument.Document;
                var upperBeam = doc.GetElement(upperBeamId);

                if (!(upperBeam.Location is LocationCurve lc)) return fallback;

                // Move o ponto da viga inferior para a cota da viga superior
                var refZ   = lc.Curve.GetEndPoint(0).Z;
                var loPtUp = new XYZ(_lowerClickPoint.X, _lowerClickPoint.Y, refZ);

                // Projeta sobre o eixo da viga superior → ponto diretamente "acima"
                var result = lc.Curve.Project(loPtUp);
                return result?.XYZPoint ?? fallback;
            }
            catch
            {
                return fallback; // se falhar, usa o ponto original do clique
            }
        }

        // ── Configuração ─────────────────────────────────────────────────────

        public double Width           { get => _width;          set => Set(ref _width, value); }
        public double TreadDepth      { get => _treadDepth;     set => Set(ref _treadDepth, value); }
        public double TreadThickness  { get => _treadThickness; set => Set(ref _treadThickness, value); }
        public bool   IncludeTreads   { get => _includeTreads;  set => Set(ref _includeTreads, value); }
        public bool   ApplyBlondel    { get => _applyBlondel;   set => Set(ref _applyBlondel, value); }
        public bool   CenterStair     { get => _centerStair;    set => Set(ref _centerStair, value); }
        public double IntermediateLandingLength { get => _intermediateLandingLength; set => Set(ref _intermediateLandingLength, value); }

        public bool HasIntermediateLanding
        {
            get => _hasIntermediateLanding;
            set
            {
                if (Set(ref _hasIntermediateLanding, value))
                    OnPropertyChanged(nameof(IsIntermediateLandingLengthEnabled));
            }
        }

        // Comprimento do patamar só é editável quando o patamar intermediário está habilitado.
        public bool IsIntermediateLandingLengthEnabled => HasIntermediateLanding;
        public string StringerPath      { get => _stringerPath;      set => Set(ref _stringerPath, value); }
        public string StringerType      { get => _stringerType;      set => Set(ref _stringerType, value); }
        public bool   UseAxis           { get => _useAxis;           set => Set(ref _useAxis, value); }
        public bool   IsChannelProfile  { get => _isChannelProfile;  set => Set(ref _isChannelProfile, value); }

        /// <summary>Tipos disponíveis do catálogo da família selecionada.</summary>
        public ObservableCollection<string> AvailableTypes { get; } = new ObservableCollection<string>();

        private void BrowseStringer()
        {
            using (var dlg = new OpenFileDialog
            {
                Title  = "Selecionar família de longarina (.rfa)",
                Filter = "Revit Family (*.rfa)|*.rfa"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                StringerPath = dlg.FileName;
                LoadTypesFromCatalog(dlg.FileName);
                AutoDetectProfileBehavior(dlg.FileName);
            }
        }

        /// <summary>
        /// Lê o catálogo de tipos (.txt) ao lado do .rfa e popula o ComboBox de perfis.
        /// Reutiliza o mesmo formato que o GerdauCatalog já conhece.
        /// </summary>
        private void LoadTypesFromCatalog(string rfaPath)
        {
            AvailableTypes.Clear();
            StringerType = null;

            var catalogPath = Path.ChangeExtension(rfaPath, ".txt");
            if (!File.Exists(catalogPath)) return;

            try
            {
                var lines = File.ReadAllLines(catalogPath, Encoding.Default);
                foreach (var line in lines.Skip(1)) // pula cabeçalho
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.TrimStart().StartsWith("##")) continue;

                    var typeName = line.Split(',')[0].Trim().Replace("\"\"", "\"");
                    if (!string.IsNullOrWhiteSpace(typeName) && !typeName.Contains("##"))
                        AvailableTypes.Add(typeName);
                }

                if (AvailableTypes.Count > 0)
                    StringerType = AvailableTypes[0]; // pré-seleciona o primeiro
            }
            catch { /* catálogo ilegível — deixa lista vazia */ }
        }

        /// <summary>
        /// Detecta pelo nome da família se é perfil U/Canal ou W/I e configura
        /// IsChannelProfile (controla IsEnabled do checkbox UseAxis).
        /// O offset geométrico é calculado automaticamente por ProfileGeometryReader.
        /// </summary>
        private void AutoDetectProfileBehavior(string rfaPath)
        {
            var familyName    = System.IO.Path.GetFileNameWithoutExtension(rfaPath);
            IsChannelProfile  = Core.Stair.ProfileGeometryReader.IsChannel(familyName);
            if (!IsChannelProfile) UseAxis = false; // W/I não usa "Usar eixo"
        }

        // ── Preview ──────────────────────────────────────────────────────────

        public StairDefinition Preview    { get => _preview;      set => Set(ref _preview, value); }
        public bool            IsCalculated { get => _isCalculated; set => Set(ref _isCalculated, value); }
        public ObservableCollection<string> Warnings { get; } = new ObservableCollection<string>();

        private bool CanCalculate()
            => _lowerBeamId != null && _upperBeamId != null;

        private void CalculatePreview()
        {
            try
            {
                var doc      = _uiApp.ActiveUIDocument.Document;
                var lower    = doc.GetElement(_lowerBeamId);
                var upper    = doc.GetElement(_upperBeamId);
                var (rise, dist) = ExtractGeometry(lower, upper);

                var config = BuildConfig();
                Preview      = StairCalculator.Calculate(rise, dist, config);
                IsCalculated = true;

                // Força o WPF a reavaliar CanExecute de todos os comandos
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();

                Warnings.Clear();
                foreach (var w in Preview.Warnings) Warnings.Add(w);
            }
            catch (Exception ex)
            {
                Warnings.Clear();
                Warnings.Add($"Erro no cálculo: {ex.Message}");
            }
        }

        // ── Criação ──────────────────────────────────────────────────────────

        private bool CanCreate() => IsCalculated && Preview?.IsValid == true;

        private void CreateStair()
        {
            // A criação usa ExternalEvent para garantir contexto válido de API do Revit.
            // Chamadas diretas de Transaction a partir de clique WPF em janela não-modal
            // não possuem contexto de API e causam crash fatal.
            _createHandler.Definition  = Preview;
            _createHandler.Config      = BuildConfig();
            _createHandler.LowerBeamId = _lowerBeamId;
            _createHandler.UpperBeamId = _upperBeamId;
            _createHandler.LowerPoint  = _lowerClickPoint;
            _createHandler.UpperPoint  = _upperClickPoint;
            _createEvent.Raise();
        }

        private void OnCreationCompleted(string error)
        {
            _dispatcher.Invoke(() =>
            {
                if (error == null)
                {
                    Warnings.Clear();
                    Warnings.Add("Escada criada com sucesso.");
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Erro ao criar escada:\n\n{error}",
                        "SAGA Structural Tools",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            });
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private StairConfig BuildConfig() => new StairConfig
        {
            Width                     = Width,
            TreadDepth                = TreadDepth,
            TreadThickness            = TreadThickness,
            ApplyBlondel              = ApplyBlondel,
            CenterStair               = CenterStair,
            IncludeTreads             = IncludeTreads,
            HasIntermediateLanding    = HasIntermediateLanding,
            IntermediateLandingLength = IntermediateLandingLength,
            StringerFamilyPath        = StringerPath,
            StringerFamilyType        = StringerType,
            UseAxis                   = UseAxis
        };

        /// <summary>
        /// Extrai desnível e distância horizontal usando os pontos projetados do clique nas vigas.
        /// O clique determina ONDE ao longo da viga a escada se conecta.
        /// </summary>
        private (double rise, double distance) ExtractGeometry(Element lower, Element upper)
        {
            var lo = _lowerClickPoint ?? FallbackPoint(lower)
                ?? throw new InvalidOperationException("Viga inferior sem geometria.");
            var up = _upperClickPoint ?? FallbackPoint(upper)
                ?? throw new InvalidOperationException("Viga superior sem geometria.");

            // Desnível: diferença de Z entre os pontos de conexão (ft → mm)
            var rise = (up.Z - lo.Z) * 304.8;

            // Distância horizontal (no plano XY) entre os pontos de conexão
            var horizVec = new XYZ(up.X - lo.X, up.Y - lo.Y, 0);
            var distance = horizVec.GetLength() * 304.8;

            return (rise, distance);
        }

        private static XYZ FallbackPoint(Element beam)
        {
            if (beam.Location is LocationCurve lc)
                return lc.Curve.Evaluate(0.5, true);
            var bb = beam.get_BoundingBox(null);
            return bb != null ? (bb.Min + bb.Max) * 0.5 : null;
        }


        public RelayCommand SelectLowerBeamCommand  { get; }
        public RelayCommand SelectUpperBeamCommand  { get; }
        public RelayCommand BrowseStringerCommand   { get; }
        public RelayCommand CalculatePreviewCommand { get; }
        public RelayCommand CreateStairCommand      { get; }
    }
}
