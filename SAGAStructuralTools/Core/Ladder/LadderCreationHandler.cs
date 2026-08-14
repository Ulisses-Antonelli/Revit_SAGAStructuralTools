using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SAGAStructuralTools.Core.Ladder
{
    /// <summary>
    /// ExternalEvent que cria (ou recria, em edição) a escada marinheiro dentro de
    /// uma transação. Na edição, os membros antigos do conjunto lógico são removidos
    /// e a escada é reconstruída com a configuração atualizada — mesma mecânica do
    /// RailCreationHandler.
    /// </summary>
    public class LadderCreationHandler : IExternalEventHandler
    {
        public LadderPlacement   Placement   { get; set; }
        public LadderConfig      Config      { get; set; }
        public LadderDefinition  Definition  { get; set; }
        public LadderEditContext EditContext { get; set; }

        public event Action<string> Completed;

        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_LadderLog.txt");

        public void Execute(UIApplication app)
        {
            Log("=== LadderCreationHandler.Execute ===");
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { Notify("Nenhum documento Revit aberto."); return; }

            if (EditContext?.SourceDocument != null && !EditContext.MatchesDocument(doc))
            {
                Notify("O documento ativo mudou. Volte ao arquivo da escada e tente novamente.");
                return;
            }

            try
            {
                string transactionName = EditContext == null
                    ? "SAGA — Gerar Escada Marinheiro"
                    : "SAGA — Atualizar Escada Marinheiro";
                using (var tx = new Transaction(doc, transactionName))
                {
                    tx.Start();
                    var failureOptions = tx.GetFailureHandlingOptions();
                    failureOptions.SetForcedModalHandling(true);
                    tx.SetFailureHandlingOptions(failureOptions);
                    try
                    {
                        ValidateRequest();

                        if (EditContext != null)
                        {
                            var previousIds = LadderAssemblyStore.FindMemberIds(doc, EditContext);
                            if (previousIds.Count == 0)
                                throw new InvalidOperationException(
                                    "Os elementos da escada selecionada não foram encontrados.");
                            doc.Delete(previousIds);
                            Log($"Edição: removidos {previousIds.Count} elementos do conjunto {EditContext.AssemblyId}.");
                        }

                        var createdIds = new List<ElementId>();
                        new LadderBuilder(doc).Build(Placement, Config, Definition, createdIds);

                        if (createdIds.Count == 0)
                            throw new InvalidOperationException(
                                "Nenhum elemento foi criado. Verifique as famílias configuradas.");

                        var stored = LadderAssemblyStore.Create(
                            Config,
                            Placement,
                            EditContext?.AssemblyId,
                            (EditContext?.Revision ?? -1) + 1);
                        LadderAssemblyStore.Attach(doc, createdIds, stored);
                        Log($"Conjunto lógico {stored.AssemblyId}: {createdIds.Count} elementos identificados.");

                        var commitStatus = tx.Commit();
                        if (commitStatus != TransactionStatus.Committed)
                            throw new InvalidOperationException(
                                "O Revit não confirmou a criação da escada marinheiro.");

                        // Contexto de edição só é atualizado após o commit; num rollback
                        // ele continua apontando para os membros antigos.
                        if (EditContext != null)
                        {
                            EditContext.Revision = stored.Revision;
                            EditContext.Config = stored.Config;
                            EditContext.Placement = Placement;
                            EditContext.MemberUniqueIds = new List<string>(stored.MemberUniqueIds);
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

        public string GetName() => "SAGACreateLadder";

        private void ValidateRequest()
        {
            if (Placement == null || !Placement.IsValid)
                throw new InvalidOperationException(
                    "Selecione a viga de referência do desembarque antes de criar a escada.");
            if (Config == null)
                throw new InvalidOperationException("A configuração da escada não está disponível.");
            if (Definition == null || !Definition.IsValid)
                throw new InvalidOperationException(
                    "O cálculo da escada é inválido. Revise os avisos do preview.");
        }

        private void Notify(string error) => Completed?.Invoke(error);

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
            catch { /* log não-crítico */ }
        }
    }
}
