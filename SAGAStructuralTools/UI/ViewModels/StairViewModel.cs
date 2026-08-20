using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core;
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
        private readonly ExternalEvent      _referencePickEvent;
        private readonly BeamPickHandler    _referencePickHandler;
        private readonly ExternalEvent      _createEvent;
        private readonly StairCreationHandler _createHandler;
        private readonly StairEditContext   _editContext;
        private bool                        _isSubmitting;

        // Estado de seleção. O papel (inferior/superior) não depende de qual botão
        // foi clicado nem da ordem do clique — é inferido comparando a cota dos
        // dois pontos só depois que as duas vigas forem escolhidas.
        private int       _pickStep;   // 0 = ocioso, 1 = aguardando a 1ª viga, 2 = aguardando a 2ª
        private ElementId _firstBeamId;
        private XYZ       _firstPoint;
        private string    _firstBeamName;

        private ElementId _lowerBeamId;
        private ElementId _upperBeamId;
        private XYZ       _lowerClickPoint;   // ponto do clique projetado no eixo da viga inferior
        private XYZ       _upperClickPoint;   // ponto do clique projetado no eixo da viga superior
        private string    _lowerBeamName = "Não selecionada";
        private string    _upperBeamName = "Não selecionada";

        // Referência opcional pro deslocamento lateral — viga ou pilar reto. Sem
        // referência, LateralOffsetMm ainda funciona como um ajuste manual simples.
        private ElementId _referenceId;
        private string    _referenceName = "Nenhuma (ajuste manual)";
        private double    _lateralOffsetMm = StairDefaults.LateralOffsetMm;

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

        public StairViewModel(UIApplication uiApp, StairEditContext editContext = null)
        {
            SagaLog.Write("StairViewModel — construtor início");
            _uiApp       = uiApp;
            _editContext = editContext;
            _dispatcher  = Dispatcher.CurrentDispatcher;

            SagaLog.Write("StairViewModel — criando BeamPickHandler...");
            _pickHandler = new BeamPickHandler();
            _pickHandler.BeamPicked += OnBeamPicked;
            SagaLog.Write("StairViewModel — ExternalEvent.Create(pick)...");
            _pickEvent = ExternalEvent.Create(_pickHandler);

            SagaLog.Write("StairViewModel — criando pick handler de referência...");
            _referencePickHandler = new BeamPickHandler(
                new BeamOrColumnFilter(),
                "Selecione a viga ou pilar de referência pro deslocamento lateral (ESC para cancelar)");
            _referencePickHandler.BeamPicked += OnReferencePicked;
            _referencePickEvent = ExternalEvent.Create(_referencePickHandler);

            SagaLog.Write("StairViewModel — criando StairCreationHandler...");
            _createHandler = new StairCreationHandler();
            _createHandler.Completed += OnCreationCompleted;
            SagaLog.Write("StairViewModel — ExternalEvent.Create(create)...");
            _createEvent = ExternalEvent.Create(_createHandler);

            SagaLog.Write("StairViewModel — criando comandos...");
            SelectBeamsCommand       = new RelayCommand(_ => StartSelection());
            SelectReferenceCommand   = new RelayCommand(_ => _referencePickEvent.Raise());
            ClearReferenceCommand    = new RelayCommand(_ => ClearReference(), _ => _referenceId != null);
            BrowseStringerCommand    = new RelayCommand(_ => BrowseStringer());
            CalculatePreviewCommand  = new RelayCommand(_ => CalculatePreview(), _ => CanCalculate());
            CreateStairCommand       = new RelayCommand(_ => CreateStair(),      _ => CanCreate());

            if (IsEditMode) LoadEditContext();
            SagaLog.Write("StairViewModel — construtor OK");
        }

        // ── Modo / títulos ─────────────────────────────────────────────────

        public bool IsEditMode => _editContext != null;
        public bool CanClose   => !_isSubmitting;
        public string WindowTitle => IsEditMode
            ? "SAGA — Editar Escada Metálica"
            : "SAGA — Gerar Escada Metálica";
        public string CreateActionText     => IsEditMode ? "Atualizar" : "Criar Escada";
        public string SelectBeamsActionText => IsEditMode ? "Substituir vigas" : "Selecionar vigas";

        /// <summary>
        /// Carrega a configuração e os pontos de conexão de uma escada existente
        /// (Alt+clique) — sem re-selecionar vigas; o usuário pode ajustar campos e
        /// clicar em Atualizar, ou usar "Substituir vigas" pra reposicionar.
        /// </summary>
        private void LoadEditContext()
        {
            if (_editContext?.Config == null) return;

            var c = _editContext.Config;
            Width                     = c.Width;
            TreadDepth                = c.TreadDepth;
            TreadThickness            = c.TreadThickness;
            ApplyBlondel              = c.ApplyBlondel;
            CenterStair               = c.CenterStair;
            IncludeTreads             = c.IncludeTreads;
            HasIntermediateLanding    = c.HasIntermediateLanding;
            IntermediateLandingLength = c.IntermediateLandingLength;
            UseAxis                   = c.UseAxis;
            LateralOffsetMm           = c.LateralOffsetMm;

            _stringerPath = c.StringerFamilyPath;
            if (!string.IsNullOrWhiteSpace(_stringerPath))
            {
                LoadTypesFromCatalog(_stringerPath);
                StringerType = c.StringerFamilyType;
                AutoDetectProfileBehavior(_stringerPath);
            }
            OnPropertyChanged(nameof(StringerPath));

            // Sem ElementId de verdade (a escada foi carregada pelos pontos salvos, não
            // por um pick novo) — usa um sentinel só pra acender o feedback visual e
            // habilitar o cálculo/criação; CreateStair() sempre usa os pontos, nunca
            // cai no fallback por ElementId quando eles já estão preenchidos.
            _lowerBeamId = ElementId.InvalidElementId;
            _upperBeamId = ElementId.InvalidElementId;
            _lowerClickPoint = _editContext.LowerPoint;
            _upperClickPoint = _editContext.UpperPoint;
            LowerBeamName = _editContext.LowerBeamName ?? "Carregada da escada existente";
            UpperBeamName = _editContext.UpperBeamName ?? "Carregada da escada existente";
            OnPropertyChanged(nameof(LowerBeamSelected));
            OnPropertyChanged(nameof(UpperBeamSelected));

            Warnings.Clear();
            Warnings.Add("Editando escada existente. Altere os campos e clique em Atualizar.");
            Warnings.Add("A atualização recria as peças; cotas, tags ou restrições ligadas a elas podem perder o vínculo.");

            CalculatePreview();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        // ── Seleção de vigas ────────────────────────────────────────────────

        public string LowerBeamName    { get => _lowerBeamName; set => Set(ref _lowerBeamName, value); }
        public string UpperBeamName    { get => _upperBeamName; set => Set(ref _upperBeamName, value); }

        // Borda do input fica verde quando selecionado (DataTrigger no XAML).
        public bool LowerBeamSelected  => _lowerBeamId != null;
        public bool UpperBeamSelected  => _upperBeamId != null;

        private void StartSelection()
        {
            _pickStep    = 1;
            _firstBeamId = null;
            _pickEvent.Raise();
        }

        private void OnBeamPicked(ElementId id, string name, XYZ axisPoint)
        {
            _dispatcher.Invoke(() =>
            {
                if (_pickStep == 1)
                {
                    // Primeira viga: ainda não sabemos se é a inferior ou a superior —
                    // só guarda e continua o laço pedindo a segunda.
                    _firstBeamId   = id;
                    _firstPoint    = axisPoint;
                    _firstBeamName = name;
                    _pickStep      = 2;
                    _pickEvent.Raise();
                    return;
                }

                if (id.Equals(_firstBeamId))
                {
                    System.Windows.MessageBox.Show(
                        "As duas vigas selecionadas são a mesma. Selecione a viga inferior e a superior separadamente.",
                        "Seleção incorreta",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    _pickStep = 1;
                    _pickEvent.Raise();
                    return;
                }

                // Segunda viga: agora dá pra inferir o papel de cada uma pela cota —
                // não importa em que ordem o usuário clicou.
                bool firstIsLower = _firstPoint.Z <= axisPoint.Z;

                _lowerBeamId     = firstIsLower ? _firstBeamId   : id;
                _lowerClickPoint = firstIsLower ? _firstPoint    : axisPoint;
                LowerBeamName    = firstIsLower ? _firstBeamName : name;

                var upperId    = firstIsLower ? id      : _firstBeamId;
                var upperPoint = firstIsLower ? axisPoint : _firstPoint;
                var upperName  = firstIsLower ? name      : _firstBeamName;

                _upperBeamId  = upperId;
                UpperBeamName = upperName;

                // Alinha o ponto de conexão superior para que a escada corra em linha
                // reta em planta — projeta o clique da viga inferior (na cota da viga
                // superior) sobre o eixo da viga superior.
                _upperClickPoint = AlignToUpperBeam(upperId, upperPoint);

                _pickStep = 0;
                OnPropertyChanged(nameof(LowerBeamSelected));
                OnPropertyChanged(nameof(UpperBeamSelected));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            });
        }

        /// <summary>
        /// Garante que o lance fique sempre ORTOGONAL ao eixo da viga inferior — não
        /// só "próximo de reto". <see cref="Curve.Project"/> numa Line limitada prende
        /// o resultado dentro do segmento físico da viga; se o pé da perpendicular cai
        /// fora da extensão modelada da viga superior, ele volta a ponta mais próxima
        /// em vez do ponto realmente ortogonal — e a escada sai diagonal. Por isso a
        /// conta é feita direto na reta infinita (sem esse "clamp"): gira o eixo da
        /// viga inferior 90° pra achar a direção do lance, e acha onde essa direção,
        /// partindo do ponto inferior, cruza o eixo (infinito) da viga superior.
        /// </summary>
        private XYZ AlignToUpperBeam(ElementId upperBeamId, XYZ fallback)
        {
            if (_lowerClickPoint == null || _lowerBeamId == null) return fallback;

            try
            {
                var doc       = _uiApp.ActiveUIDocument.Document;
                var lowerBeam = doc.GetElement(_lowerBeamId);
                var upperBeam = doc.GetElement(upperBeamId);

                if (!(lowerBeam.Location is LocationCurve loLc) || !(loLc.Curve is Line loLine)) return fallback;
                if (!(upperBeam.Location is LocationCurve upLc) || !(upLc.Curve is Line upLine)) return fallback;

                var lowerDir = new XYZ(loLine.Direction.X, loLine.Direction.Y, 0);
                if (lowerDir.GetLength() < 1e-9) return fallback; // viga inferior vertical — sem direção em planta
                lowerDir = lowerDir.Normalize();

                // Direção do lance: perpendicular ao eixo da viga inferior, em planta.
                var runDir = new XYZ(lowerDir.Y, -lowerDir.X, 0);

                // Não inverte o sentido do lance — usa o clique bruto da viga superior
                // só pra saber pra que lado (runDir ou -runDir) a escada deve subir.
                var lowerXY = new XYZ(_lowerClickPoint.X, _lowerClickPoint.Y, 0);
                var rawUpperXY = new XYZ(fallback.X, fallback.Y, 0);
                if ((rawUpperXY - lowerXY).DotProduct(runDir) < 0) runDir = runDir.Negate();

                // Interseção da reta (lowerXY, runDir) com a reta infinita do eixo da viga
                // superior — resolvido em planta, sem clamp ao segmento físico da viga.
                var q0 = new XYZ(upLine.GetEndPoint(0).X, upLine.GetEndPoint(0).Y, 0);
                var q1 = new XYZ(upLine.GetEndPoint(1).X, upLine.GetEndPoint(1).Y, 0);
                var upperDirXY = q1 - q0;

                double denom = runDir.X * upperDirXY.Y - runDir.Y * upperDirXY.X;
                if (Math.Abs(denom) < 1e-9) return fallback; // viga superior paralela ao lance — sem cruzamento

                double t = ((q0.X - lowerXY.X) * runDir.Y - (q0.Y - lowerXY.Y) * runDir.X) / denom;
                var upperPointXY = q0 + upperDirXY * t;

                // Cota interpolada ao longo do eixo real da viga superior (cobre inclinação;
                // t é o próprio parâmetro normalizado de q0 a q1, não precisa recalcular).
                double z = upLine.GetEndPoint(0).Z + (upLine.GetEndPoint(1).Z - upLine.GetEndPoint(0).Z) * t;

                return new XYZ(upperPointXY.X, upperPointXY.Y, z);
            }
            catch
            {
                return fallback; // se falhar, usa o ponto original do clique
            }
        }

        // ── Deslocamento lateral ────────────────────────────────────────────

        public string ReferenceName     { get => _referenceName; set => Set(ref _referenceName, value); }
        public bool   ReferenceSelected => _referenceId != null;
        public double LateralOffsetMm   { get => _lateralOffsetMm; set => Set(ref _lateralOffsetMm, value); }

        private void OnReferencePicked(ElementId id, string name, XYZ axisPoint)
        {
            _dispatcher.Invoke(() =>
            {
                _referenceId  = id;
                ReferenceName = name;
                OnPropertyChanged(nameof(ReferenceSelected));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            });
        }

        private void ClearReference()
        {
            _referenceId  = null;
            ReferenceName = "Nenhuma (ajuste manual)";
            OnPropertyChanged(nameof(ReferenceSelected));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Desloca os pontos de conexão (brutos, sobre o eixo de cada viga) perpendicularmente
        /// ao sentido do lance, deixando o conjunto inteiro (as duas vigas de conexão) fora do
        /// centro do clique quando necessário. Sem referência selecionada, aplica só o valor
        /// digitado em <see cref="LateralOffsetMm"/> como ajuste manual; com referência, soma a
        /// posição lateral dela (eixo da viga/pilar de referência) ao valor digitado.
        /// </summary>
        private void GetShiftedConnectionPoints(out XYZ lower, out XYZ upper)
        {
            lower = _lowerClickPoint;
            upper = _upperClickPoint;
            if (lower == null || upper == null) return;

            var runDirXY = new XYZ(upper.X - lower.X, upper.Y - lower.Y, 0);
            if (runDirXY.GetLength() < 1e-6) return; // lance vertical em planta — sem direção lateral definida
            runDirXY = runDirXY.Normalize();
            var lateralDir = new XYZ(runDirXY.Y, -runDirXY.X, 0);

            double shiftFt = LateralOffsetMm / 304.8;

            if (_referenceId != null)
            {
                try
                {
                    var doc       = _uiApp.ActiveUIDocument.Document;
                    var reference = Core.Rail.RoundedCornerMember.Get(doc, _referenceId, "referência");
                    var axis      = reference.GetAxis();
                    var refMidXY  = new XYZ(
                        (axis.GetEndPoint(0).X + axis.GetEndPoint(1).X) / 2.0,
                        (axis.GetEndPoint(0).Y + axis.GetEndPoint(1).Y) / 2.0,
                        0);
                    var lowerXY = new XYZ(lower.X, lower.Y, 0);
                    shiftFt += (refMidXY - lowerXY).DotProduct(lateralDir);
                }
                catch
                {
                    // Referência inválida (ex.: elemento apagado) — segue só com o ajuste manual.
                }
            }

            if (Math.Abs(shiftFt) < 1e-9) return;

            var shiftVec = lateralDir * shiftFt;
            lower += shiftVec;
            upper += shiftVec;
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
                var lines = CatalogTextReader.ReadAllLines(catalogPath);
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
            GetShiftedConnectionPoints(out var lowerPt, out var upperPt);

            _createHandler.Definition    = Preview;
            _createHandler.Config        = BuildConfig();
            _createHandler.LowerBeamId   = _lowerBeamId;
            _createHandler.UpperBeamId   = _upperBeamId;
            _createHandler.LowerPoint    = lowerPt;
            _createHandler.UpperPoint    = upperPt;
            _createHandler.LowerBeamName = LowerBeamName;
            _createHandler.UpperBeamName = UpperBeamName;
            _createHandler.EditContext   = _editContext;

            _isSubmitting = true;
            OnPropertyChanged(nameof(CanClose));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            _createEvent.Raise();
        }

        private void OnCreationCompleted(string error)
        {
            _dispatcher.Invoke(() =>
            {
                _isSubmitting = false;
                OnPropertyChanged(nameof(CanClose));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();

                if (error == null)
                {
                    Warnings.Clear();
                    Warnings.Add(IsEditMode ? "Escada atualizada com sucesso." : "Escada criada com sucesso.");
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
            UseAxis                   = UseAxis,
            LateralOffsetMm           = LateralOffsetMm
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


        public RelayCommand SelectBeamsCommand      { get; }
        public RelayCommand SelectReferenceCommand  { get; }
        public RelayCommand ClearReferenceCommand   { get; }
        public RelayCommand BrowseStringerCommand   { get; }
        public RelayCommand CalculatePreviewCommand { get; }
        public RelayCommand CreateStairCommand      { get; }
    }
}
