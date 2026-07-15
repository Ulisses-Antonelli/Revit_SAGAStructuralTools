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

                            if (EditContext != null)
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

                            // Os offsets globais pertencem apenas ao fluxo inclinado. Isso evita
                            // que um preset desse modo desloque silenciosamente o comando horizontal.
                            var run = IsInclinedRun
                                ? referenceRun.OffsetMm(
                                    Config?.GlobalVerticalOffset ?? 0.0,
                                    Config?.GlobalLateralOffset ?? 0.0)
                                : referenceRun;

                            var createdIds = new List<ElementId>();

                            // Corrimão primeiro: mede o eixo central real e repassa aos
                            // montantes (topo) e travessas (distribuição), evitando o chute
                            // de dimensão por nome de parâmetro.
                            try
                            {
                                var railAxisRun = handrailBuilder.Build(seg, Config, run, createdIds);
                                postBuilder.Build(seg, Config, run, railAxisRun, createdIds);
                                infillBuilder.Build(seg, Config, run, railAxisRun, createdIds);

                                if (createdIds.Count == 0)
                                    throw new InvalidOperationException(
                                        "Nenhum elemento foi criado. Verifique as famílias configuradas.");
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException(
                                    $"Trecho {i + 1}: {ex.Message}", ex);
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

        private void Notify(string error) => Completed?.Invoke(error);

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
            catch { }
        }
    }
}
