using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SAGAStructuralTools.Core.Rail
{
    public class RailCreationHandler : IExternalEventHandler
    {
        public RailDefinition  Definition  { get; set; }
        public RailConfig      Config      { get; set; }
        public List<ElementId> SegmentIds  { get; set; }
        public RailEditContext EditContext { get; set; }
        public bool            ReplaceEditBaseLine { get; set; }
        public bool            IsInclinedRun { get; set; }

        public event Action<string> Completed;

        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_RailLog.txt");

        public void Execute(UIApplication app)
        {
            Log("=== RailCreationHandler.Execute ===");
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { Notify("Nenhum documento Revit aberto."); return; }

            if (EditContext?.SourceDocument != null &&
                !EditContext.MatchesDocument(doc))
            {
                Notify("O documento ativo mudou. Volte ao arquivo do guarda-corpo e tente novamente.");
                return;
            }

            try
            {
                string transactionName = EditContext == null
                    ? "SAGA — Gerar Guarda-Corpo Metálico"
                    : "SAGA — Atualizar Guarda-Corpo Metálico";
                using (var tx = new Transaction(doc, transactionName))
                {
                    tx.Start();
                    var failureOptions = tx.GetFailureHandlingOptions();
                    failureOptions.SetForcedModalHandling(true);
                    tx.SetFailureHandlingOptions(failureOptions);
                    try
                    {
                        ValidateRequest();

                        RailAssemblyData updatedEditData = null;
                        var postBuilder     = new PostBuilder(doc);
                        var handrailBuilder = new HandrailBuilder(doc);
                        var infillBuilder   = new InfillBuilder(doc);

                        if (EditContext != null)
                        {
                            int removedCorners = RoundedCornerStore.RemoveForAssembly(
                                doc, EditContext.AssemblyId);
                            if (removedCorners > 0)
                                Log($"Edicao: removidos {removedCorners} cantos arredondados manuais vinculados.");

                            var previousIds = RailAssemblyStore.FindMemberIds(doc, EditContext);
                            if (previousIds.Count == 0)
                                throw new InvalidOperationException(
                                    "Os elementos do guarda-corpo selecionado não foram encontrados.");

                            doc.Delete(previousIds);
                            Log($"Edição: removidos {previousIds.Count} elementos do conjunto {EditContext.AssemblyId}.");
                        }

                        for (int i = 0; i < Definition.Segments.Count; i++)
                        {
                            var seg = Definition.Segments[i];
                            XYZ start;
                            XYZ end;

                            if (EditContext != null && !ReplaceEditBaseLine)
                            {
                                start = EditContext.Start;
                                end = EditContext.End;
                            }
                            else
                            {
                                if (SegmentIds == null || i >= SegmentIds.Count)
                                    throw new InvalidOperationException(
                                        $"Trecho {i + 1}: a referência da viga selecionada não está disponível.");

                                var selectedId = SegmentIds[i];
                                var lineEl = doc.GetElement(selectedId);
                                if (lineEl == null)
                                    throw new InvalidOperationException(
                                        $"Trecho {i + 1} (elemento {selectedId}): a viga selecionada não foi encontrada.");
                                if (!LinePickHandler.TryGetBoundLine(lineEl, out var line))
                                    throw new InvalidOperationException(
                                        $"Trecho {i + 1} (elemento {selectedId}): o elemento não possui mais um eixo reto válido.");
                                start = line.GetEndPoint(0);
                                end = line.GetEndPoint(1);
                            }

                            if (start == null || end == null || start.DistanceTo(end) < 0.001)
                                throw new InvalidOperationException("A linha-base do guarda-corpo é inválida.");

                            // Mantém o eixo escolhido separado do eixo efetivo de criação.
                            // O primeiro é persistido para que uma edição reaplique a configuração
                            // exatamente uma vez, sem acumular o deslocamento global.
                            var referenceRun = RailRunGeometry.Create(start, end);
                            if (IsInclinedRun && !referenceRun.IsInclined)
                                throw new InvalidOperationException(
                                    "O comando de guarda-corpo inclinado exige uma linha-base com desnível. " +
                                    "Selecione uma longarina ou linha 3D inclinada.");
                            if (!IsInclinedRun && EditContext == null && referenceRun.IsInclined)
                                throw new InvalidOperationException(
                                    "A linha selecionada é inclinada. Use o comando 'Guarda-corpo inclinado' " +
                                    "para gerar este trecho.");

                            var createdIds = new List<ElementId>();
                            var creationRuns = BuildCreationRuns(referenceRun);

                            for (int runIndex = 0; runIndex < creationRuns.Count; runIndex++)
                            {
                                var creationReferenceRun = creationRuns[runIndex];

                                // Os offsets globais pertencem apenas ao fluxo inclinado. Isso evita
                                // que um preset desse modo desloque silenciosamente o comando horizontal.
                                // No par, a normal positiva de cada eixo aponta para fora da escada;
                                // portanto o mesmo valor lateral produz offsets realmente espelhados.
                                var run = IsInclinedRun
                                    ? creationReferenceRun.OffsetMm(
                                        Config?.GlobalVerticalOffset ?? 0.0,
                                        Config?.GlobalLateralOffset ?? 0.0)
                                    : creationReferenceRun;

                                int countBeforeRun = createdIds.Count;

                                // Corrimão primeiro: mede o eixo central real e repassa aos
                                // montantes (topo) e travessas (distribuição), evitando o chute
                                // de dimensão por nome de parâmetro.
                                try
                                {
                                    var railAxisRun = handrailBuilder.Build(seg, Config, run, createdIds);
                                    postBuilder.Build(seg, Config, run, railAxisRun, createdIds);
                                    infillBuilder.Build(seg, Config, run, railAxisRun, createdIds);

                                    if (createdIds.Count == countBeforeRun)
                                        throw new InvalidOperationException(
                                            "Nenhum elemento foi criado. Verifique as famílias configuradas.");
                                }
                                catch (Exception ex)
                                {
                                    string side = creationRuns.Count > 1
                                        ? $", lado {runIndex + 1} do par"
                                        : "";
                                    throw new InvalidOperationException(
                                        $"Trecho {i + 1}{side}: {ex.Message}", ex);
                                }
                            }

                            var stored = RailAssemblyStore.Create(
                                Config,
                                referenceRun.Start,
                                referenceRun.End,
                                EditContext?.AssemblyId,
                                (EditContext?.Revision ?? -1) + 1);
                            RailAssemblyStore.Attach(doc, createdIds, stored);

                            if (EditContext != null) updatedEditData = stored;
                            Log($"Conjunto lógico {stored.AssemblyId}: {createdIds.Count} elementos identificados.");
                        }

                        var commitStatus = tx.Commit();
                        if (commitStatus != TransactionStatus.Committed)
                            throw new InvalidOperationException(
                                "O Revit não confirmou a atualização do guarda-corpo.");

                        // Só atualiza o contexto mantido pela janela depois do commit.
                        // Em caso de rollback ele continua apontando para os membros antigos.
                        if (EditContext != null && updatedEditData != null)
                        {
                            EditContext.Revision = updatedEditData.Revision;
                            EditContext.Config = updatedEditData.Config;
                            EditContext.Start = updatedEditData.Start.ToXyz();
                            EditContext.End = updatedEditData.End.ToXyz();
                            EditContext.MemberUniqueIds =
                                new List<string>(updatedEditData.MemberUniqueIds);
                        }
                        Log("=== Concluído com sucesso ===");
                        Notify(null);
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        Log($"ERRO no Build: {ex.Message}\n{ex.StackTrace}");
                        Notify(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"ERRO externo: {ex.Message}");
                Notify(ex.Message);
            }
        }

        public string GetName() => "SAGACreateRail";

        private void ValidateRequest()
        {
            if (Definition?.Segments == null || Definition.Segments.Count == 0)
                throw new InvalidOperationException(
                    "Nenhum trecho válido foi informado para criar o guarda-corpo.");
            if (Config == null)
                throw new InvalidOperationException(
                    "A configuração do guarda-corpo não está disponível.");

            bool storedPair = EditContext?.Config?.MirrorPairEnabled == true;
            if (storedPair)
            {
                // Um conjunto que nasceu como par continua sendo reconstruído como par.
                // Um vetor recebido da janela pode redefinir os eixos. O payload é
                // usado apenas como fallback quando a requisição não traz referência.
                Config.MirrorPairEnabled = true;
                if (!Config.MirrorReferenceDefined &&
                    EditContext.Config.MirrorReferenceDefined)
                {
                    Config.MirrorReferenceDefined = true;
                    Config.MirrorTranslationX = EditContext.Config.MirrorTranslationX;
                    Config.MirrorTranslationY = EditContext.Config.MirrorTranslationY;
                }

                // Payloads do protótipo anterior não possuem vetor explícito.
                // Se a migração automática não foi possível e a janela também não
                // definiu eixos, mantém o mecanismo legado.
                else if (!Config.MirrorReferenceDefined)
                {
                    Config.MirrorStairWidth = EditContext.Config.MirrorStairWidth;
                    Config.MirrorWidthIsAxis = EditContext.Config.MirrorWidthIsAxis;
                    Config.MirrorInvertSide = EditContext.Config.MirrorInvertSide;
                    Config.MirrorProfileWidth = EditContext.Config.MirrorProfileWidth;
                    Config.MirrorBaseSideSign = NormalizeSideSign(
                        EditContext.Config.MirrorBaseSideSign);
                }
            }

            if (!Config.MirrorPairEnabled) return;

            if (!IsInclinedRun)
                throw new InvalidOperationException(
                    "O par espelhado está disponível somente no comando de guarda-corpo inclinado.");
            if (EditContext == null &&
                (SegmentIds == null || SegmentIds.Count != Definition.Segments.Count))
                throw new InvalidOperationException(
                    "Para criar pares espelhados, selecione uma ou mais longarinas inclinadas válidas.");
            if (EditContext != null && Definition.Segments.Count != 1)
                throw new InvalidOperationException(
                    "A edição de um par espelhado deve reconstruir somente o trecho selecionado.");
            if (EditContext != null && !storedPair)
                throw new InvalidOperationException(
                    "O par espelhado deve ser definido na criação inicial do guarda-corpo. " +
                    "Selecione os dois eixos de referência e crie um novo conjunto.");

            bool legacyEdit = EditContext != null && storedPair &&
                              !EditContext.Config.MirrorReferenceDefined &&
                              !Config.MirrorReferenceDefined;
            if (!legacyEdit)
            {
                if (!Config.MirrorReferenceDefined)
                    throw new InvalidOperationException(
                        "Defina o par selecionando primeiro o eixo fonte e depois o eixo oposto.");
                if (!IsFinite(Config.MirrorTranslationX) ||
                    !IsFinite(Config.MirrorTranslationY))
                    throw new InvalidOperationException(
                        "O vetor entre os eixos de referência é inválido.");

                double translationLengthMm = Math.Sqrt(
                    Config.MirrorTranslationX * Config.MirrorTranslationX +
                    Config.MirrorTranslationY * Config.MirrorTranslationY);
                if (translationLengthMm <= 1.0)
                    throw new InvalidOperationException(
                        "Os dois eixos de referência estão coincidentes ou muito próximos. " +
                        "Selecione eixos opostos com distância maior que 1 mm.");
            }
        }

        private List<RailRunGeometry> BuildCreationRuns(RailRunGeometry referenceRun)
        {
            if (!Config.MirrorPairEnabled)
                return new List<RailRunGeometry> { referenceRun };

            if (!referenceRun.IsInclined)
                throw new InvalidOperationException(
                    "O par espelhado exige uma longarina inclinada.");

            if (!Config.MirrorReferenceDefined)
                return BuildLegacyCreationRuns(referenceRun);

            var translation = new XYZ(
                Config.MirrorTranslationX / 304.8,
                Config.MirrorTranslationY / 304.8,
                0.0);
            double translationLengthFt = translation.GetLength();
            double longitudinalProjectionFt = Math.Abs(
                translation.DotProduct(referenceRun.HorizontalDirection));
            const double perpendicularToleranceRadians = Math.PI / 180.0;
            if (longitudinalProjectionFt >
                translationLengthFt * Math.Sin(perpendicularToleranceRadians))
                throw new InvalidOperationException(
                    "O vetor definido pelos dois eixos não é perpendicular a um dos " +
                    "trechos selecionados. Selecione somente longarinas paralelas ou " +
                    "faça uma criação separada para esse trecho.");

            double lateralProjectionFt = translation.DotProduct(referenceRun.Lateral);
            if (Math.Abs(lateralProjectionFt) * 304.8 <= 1.0)
                throw new InvalidOperationException(
                    "O vetor entre os eixos não possui afastamento transversal suficiente " +
                    "para este trecho. Verifique os dois eixos de referência.");

            int pairSideSign = lateralProjectionFt >= 0.0 ? 1 : -1;

            // A fonte aponta para fora no sentido oposto ao vetor; o eixo transladado
            // aponta para fora no sentido do vetor. O mesmo offset lateral se espelha.
            var sourceRun = referenceRun.WithLateralOrientation(-pairSideSign);
            var pairedRun = referenceRun
                .Translate(translation)
                .WithLateralOrientation(pairSideSign);

            Log($"Par por eixos: dX={Config.MirrorTranslationX:F1}mm | " +
                $"dY={Config.MirrorTranslationY:F1}mm | " +
                $"distância={translation.GetLength() * 304.8:F1}mm | " +
                $"projeção lateral={lateralProjectionFt * 304.8:F1}mm | " +
                $"sign={pairSideSign}");

            return new List<RailRunGeometry> { sourceRun, pairedRun };
        }

        private static int NormalizeSideSign(int sign) => sign < 0 ? -1 : 1;

        private List<RailRunGeometry> BuildLegacyCreationRuns(
            RailRunGeometry referenceRun)
        {
            double axisSpacingMm = Config.MirrorWidthIsAxis
                ? Config.MirrorStairWidth
                : Config.MirrorStairWidth + Config.MirrorProfileWidth;
            if (axisSpacingMm <= 1.0)
                throw new InvalidOperationException(
                    "O par legado não possui uma distância entre eixos válida. " +
                    "Defina novamente os dois eixos de referência.");

            int effectiveSideSign = NormalizeSideSign(Config.MirrorBaseSideSign)
                * (Config.MirrorInvertSide ? -1 : 1);
            var sourceRun = referenceRun.WithLateralOrientation(-effectiveSideSign);
            var pairedRun = referenceRun
                .Offset(0.0, effectiveSideSign * axisSpacingMm / 304.8)
                .WithLateralOrientation(effectiveSideSign);

            Log($"Par legado: eixos={axisSpacingMm:F1}mm | sign={effectiveSideSign}");
            return new List<RailRunGeometry> { sourceRun, pairedRun };
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private void Notify(string error) => Completed?.Invoke(error);

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
            catch { }
        }
    }
}
