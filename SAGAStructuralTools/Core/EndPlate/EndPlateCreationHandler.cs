using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.EndPlate
{
    /// <summary>
    /// ExternalEvent que cria (ou recria, em edição) a(s) chapa(s) de topo dentro
    /// de uma transação. Na edição, os membros antigos do conjunto lógico são
    /// removidos e a chapa é reconstruída com a configuração atualizada — mesma
    /// mecânica de <see cref="Stiffener.StiffenerCreationHandler"/>.
    /// </summary>
    public class EndPlateCreationHandler : IExternalEventHandler
    {
        public EndPlatePlacement   Placement   { get; set; }
        public EndPlateConfig      Config      { get; set; }
        public EndPlateEditContext EditContext { get; set; }

        public event Action<string> Completed;

        public void Execute(UIApplication app)
        {
            SagaLog.Write("=== EndPlateCreationHandler.Execute ===");
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { Notify("Nenhum documento Revit aberto."); return; }

            if (EditContext?.SourceDocument != null && !EditContext.MatchesDocument(doc))
            {
                Notify("O documento ativo mudou. Volte ao arquivo da chapa e tente novamente.");
                return;
            }

            try
            {
                string transactionName = EditContext == null
                    ? "SAGA — Gerar Chapa de Topo"
                    : "SAGA — Atualizar Chapa de Topo";
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
                            var previousIds = EndPlateAssemblyStore.FindMemberIds(doc, EditContext);
                            if (previousIds.Count == 0)
                                throw new InvalidOperationException(
                                    "Os elementos da chapa selecionada não foram encontrados.");
                            doc.Delete(previousIds);
                            SagaLog.Write($"Edição: removidos {previousIds.Count} elementos do conjunto {EditContext.AssemblyId}.");
                        }

                        var createdIds = new List<ElementId>();
                        new EndPlateBuilder(doc).Build(Placement, Config, createdIds);

                        if (createdIds.Count == 0)
                            throw new InvalidOperationException("Nenhuma chapa foi criada.");

                        var stored = EndPlateAssemblyStore.Create(
                            Config,
                            Placement,
                            EditContext?.AssemblyId,
                            (EditContext?.Revision ?? -1) + 1);
                        EndPlateAssemblyStore.Attach(doc, createdIds, stored);

                        var commitStatus = tx.Commit();
                        if (commitStatus != TransactionStatus.Committed)
                            throw new InvalidOperationException("O Revit não confirmou a criação da chapa.");

                        if (EditContext != null)
                        {
                            EditContext.Revision = stored.Revision;
                            EditContext.Config = stored.Config;
                            EditContext.MemberUniqueIds = new List<string>(stored.MemberUniqueIds);
                        }

                        SagaLog.Write($"Chapa de topo {stored.AssemblyId}: {createdIds.Count} elemento(s) criado(s).");
                        Notify(null);
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        SagaLog.Exception("EndPlateCreationHandler.Build", ex);
                        Notify(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                SagaLog.Exception("EndPlateCreationHandler.Execute", ex);
                Notify(ex.Message);
            }
        }

        public string GetName() => "SAGACreateEndPlate";

        private void ValidateRequest()
        {
            if (Placement == null || !Placement.IsValid)
                throw new InvalidOperationException("Selecione a(s) peça(s) antes de criar a chapa.");
            if (Config == null)
                throw new InvalidOperationException("A configuração da chapa não está disponível.");
        }

        private void Notify(string error) => Completed?.Invoke(error);
    }
}
