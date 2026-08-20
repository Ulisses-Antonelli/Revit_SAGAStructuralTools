using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Stiffener
{
    /// <summary>
    /// ExternalEvent que cria (ou recria, em edição) a(s) chapa(s) de nervura dentro
    /// de uma transação. Na edição, os membros antigos do conjunto lógico são
    /// removidos e a nervura é reconstruída com a configuração atualizada — mesma
    /// mecânica de <see cref="Ladder.LadderCreationHandler"/>.
    /// </summary>
    public class StiffenerCreationHandler : IExternalEventHandler
    {
        public StiffenerPlacement   Placement   { get; set; }
        public StiffenerConfig      Config      { get; set; }
        public StiffenerDefinition  Definition  { get; set; }
        public StiffenerEditContext EditContext { get; set; }

        public event Action<string> Completed;

        public void Execute(UIApplication app)
        {
            SagaLog.Write("=== StiffenerCreationHandler.Execute ===");
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { Notify("Nenhum documento Revit aberto."); return; }

            if (EditContext?.SourceDocument != null && !EditContext.MatchesDocument(doc))
            {
                Notify("O documento ativo mudou. Volte ao arquivo da nervura e tente novamente.");
                return;
            }

            try
            {
                string transactionName = EditContext == null
                    ? "SAGA — Gerar Nervura"
                    : "SAGA — Atualizar Nervura";
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
                            var previousIds = StiffenerAssemblyStore.FindMemberIds(doc, EditContext);
                            if (previousIds.Count == 0)
                                throw new InvalidOperationException(
                                    "Os elementos da nervura selecionada não foram encontrados.");
                            doc.Delete(previousIds);
                            SagaLog.Write($"Edição: removidos {previousIds.Count} elementos do conjunto {EditContext.AssemblyId}.");
                        }

                        var createdIds = new List<ElementId>();
                        new StiffenerBuilder(doc).Build(Definition, Placement, Config, createdIds);

                        if (createdIds.Count == 0)
                            throw new InvalidOperationException("Nenhuma chapa foi criada.");

                        var stored = StiffenerAssemblyStore.Create(
                            Config,
                            Placement,
                            EditContext?.AssemblyId,
                            (EditContext?.Revision ?? -1) + 1);
                        StiffenerAssemblyStore.Attach(doc, createdIds, stored);

                        var commitStatus = tx.Commit();
                        if (commitStatus != TransactionStatus.Committed)
                            throw new InvalidOperationException("O Revit não confirmou a criação da nervura.");

                        if (EditContext != null)
                        {
                            EditContext.Revision = stored.Revision;
                            EditContext.Config = stored.Config;
                            EditContext.Placement = Placement;
                            EditContext.MemberUniqueIds = new List<string>(stored.MemberUniqueIds);
                        }

                        SagaLog.Write($"Nervura {stored.AssemblyId}: {createdIds.Count} chapa(s) na viga {Placement.BeamName}.");
                        Notify(null);
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                        SagaLog.Exception("StiffenerCreationHandler.Build", ex);
                        Notify(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                SagaLog.Exception("StiffenerCreationHandler.Execute", ex);
                Notify(ex.Message);
            }
        }

        public string GetName() => "SAGACreateStiffener";

        private void ValidateRequest()
        {
            if (Placement == null || !Placement.IsValid)
                throw new InvalidOperationException("Selecione a viga W antes de criar a nervura.");
            if (Config == null)
                throw new InvalidOperationException("A configuração da nervura não está disponível.");
            if (Definition == null || !Definition.IsValid)
                throw new InvalidOperationException("O cálculo da nervura é inválido. Revise os avisos do preview.");
        }

        private void Notify(string error) => Completed?.Invoke(error);
    }
}
