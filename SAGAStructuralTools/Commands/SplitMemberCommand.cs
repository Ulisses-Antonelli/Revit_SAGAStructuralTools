using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Alignment;
using SAGAStructuralTools.Core.Rail;
using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Commands
{
    /// <summary>
    /// Interrompe vigas ou pilares retos no ponto de interseção com o eixo de um
    /// elemento de referência (viga ou pilar). Fluxo: um clique no elemento de
    /// referência, depois seleção múltipla dos elementos a dividir (Concluir na
    /// barra de opções). O laço continua pedindo novas referências até Esc.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SplitMemberCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDocument = commandData?.Application?.ActiveUIDocument;
            var document = uiDocument?.Document;
            if (document == null)
            {
                message = "Nenhum documento Revit está aberto.";
                return Result.Failed;
            }

            try
            {
                return RunContinuous(uiDocument, document);
            }
            catch (Exception ex)
            {
                SagaLog.Exception("SplitMemberCommand.Execute", ex);
                TaskDialog.Show(
                    "SAGA - Interromper viga/pilar",
                    $"A ferramenta foi encerrada por um erro inesperado:\n\n{ex.Message}");
                return Result.Cancelled;
            }
        }

        private static Result RunContinuous(UIDocument uiDocument, Document document)
        {
            var filter = new ExtendableComponentFilter();
            int splitCount = 0;

            while (true)
            {
                Reference referenceReference;
                IList<Reference> targetReferences;
                try
                {
                    string hint = splitCount > 0 ? $" ({splitCount} dividido(s); Esc encerra)" : " (Esc encerra)";
                    referenceReference = uiDocument.Selection.PickObject(
                        ObjectType.Element, filter,
                        "Selecione a viga ou pilar de referência (ponto de corte)" + hint);

                    // Limpa a seleção antes do passo seguinte: senão o elemento de
                    // referência continua realçado e entra junto na seleção múltipla.
                    uiDocument.Selection.SetElementIds(new List<ElementId>());

                    targetReferences = uiDocument.Selection.PickObjects(
                        ObjectType.Element, filter,
                        "Selecione as vigas ou pilares a dividir e clique em Concluir na barra de opções" + hint);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return splitCount > 0 ? Result.Succeeded : Result.Cancelled;
                }

                var failures = new List<string>();

                foreach (var targetReference in targetReferences)
                {
                    try
                    {
                        using (var tx = new Transaction(document, "SAGA - Interromper viga/pilar"))
                        {
                            tx.Start();
                            try
                            {
                                SplitMemberService.Split(document, referenceReference.ElementId, targetReference.ElementId);
                                tx.Commit();
                            }
                            catch
                            {
                                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                                throw;
                            }
                        }

                        splitCount++;
                        SagaLog.Write(
                            $"SplitMemberCommand: elemento {targetReference.ElementId.GetId()} " +
                            $"dividido no cruzamento com {referenceReference.ElementId.GetId()}.");
                    }
                    catch (Exception ex)
                    {
                        SagaLog.Exception("SplitMemberCommand.Split", ex);
                        failures.Add($"Elemento {targetReference.ElementId.GetId()}: {ex.Message}");
                    }
                }

                // Uma mensagem só no fim do lote, em vez de um diálogo por peça: com
                // seleção múltipla, um erro repetido (ex.: eixos paralelos em planta)
                // viraria uma fila de pop-ups pra fechar um a um.
                if (failures.Count > 0)
                {
                    TaskDialog.Show(
                        "SAGA - Interromper viga/pilar",
                        $"{failures.Count} de {targetReferences.Count} não foi(ram) dividido(s):\n\n" +
                        string.Join("\n", failures) + "\n\n" +
                        "Selecione outro conjunto ou pressione Esc para encerrar.");
                }
            }
        }

        private sealed class ExtendableComponentFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => RoundedCornerMember.IsSelectable(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
