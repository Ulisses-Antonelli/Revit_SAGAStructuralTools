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
                !ReferenceEquals(EditContext.SourceDocument, doc))
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
                                if (SegmentIds == null || i >= SegmentIds.Count) continue;
                                var lineEl = doc.GetElement(SegmentIds[i]);
                                if (!(lineEl?.Location is LocationCurve lc)) continue;
                                if (!(lc.Curve is Line line)) continue;
                                start = line.GetEndPoint(0);
                                end = line.GetEndPoint(1);
                            }

                            if (start == null || end == null || start.DistanceTo(end) < 0.001)
                                throw new InvalidOperationException("A linha-base do guarda-corpo é inválida.");

                            var createdIds = new List<ElementId>();

                            // Corrimão primeiro: mede o eixo central real e repassa aos
                            // montantes (topo) e travessas (distribuição), evitando o chute
                            // de dimensão por nome de parâmetro.
                            double? railAxisZ = handrailBuilder.Build(seg, Config, start, end, createdIds);
                            postBuilder.Build(seg, Config, start, end, railAxisZ, createdIds);
                            infillBuilder.Build(seg, Config, start, end, railAxisZ, createdIds);

                            if (createdIds.Count == 0)
                                throw new InvalidOperationException(
                                    "Nenhum elemento foi criado para o trecho. Verifique as famílias configuradas.");

                            var stored = RailAssemblyStore.Create(
                                Config,
                                start,
                                end,
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
