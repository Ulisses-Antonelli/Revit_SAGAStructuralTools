using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Alignment;
using SAGAStructuralTools.Core.Rail;
using System;

namespace SAGAStructuralTools.Commands
{
    /// <summary>
    /// Estica a extremidade mais próxima de um terceiro elemento (ex.: cantoneira de
    /// contraventamento) até o ponto de trabalho de duas vigas/pilares que se cruzam —
    /// sem transladar a peça inteira. Fluxo: primeira viga, segunda viga, elemento a
    /// alinhar — três cliques sequenciais, igual ao Arredondar Canto.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignToWorkPointCommand : IExternalCommand
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
                SagaLog.Exception("AlignToWorkPointCommand.Execute", ex);
                TaskDialog.Show(
                    "SAGA - Alinhar ao ponto de trabalho",
                    $"A ferramenta foi encerrada por um erro inesperado:\n\n{ex.Message}");
                return Result.Cancelled;
            }
        }

        private static Result RunContinuous(UIDocument uiDocument, Document document)
        {
            var filter = new ExtendableComponentFilter();
            int alignedCount = 0;

            while (true)
            {
                Reference firstReference;
                Reference secondReference;
                Reference targetReference;
                try
                {
                    string hint = alignedCount > 0 ? $" ({alignedCount} alinhado(s); Esc encerra)" : " (Esc encerra)";
                    firstReference = uiDocument.Selection.PickObject(
                        ObjectType.Element, filter,
                        "Selecione a primeira viga do cruzamento" + hint);
                    secondReference = uiDocument.Selection.PickObject(
                        ObjectType.Element, filter,
                        "Selecione a segunda viga do cruzamento" + hint);
                    targetReference = uiDocument.Selection.PickObject(
                        ObjectType.Element, filter,
                        "Selecione o elemento a esticar até o ponto de trabalho" + hint);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return alignedCount > 0 ? Result.Succeeded : Result.Cancelled;
                }

                try
                {
                    using (var tx = new Transaction(document, "SAGA - Alinhar ao ponto de trabalho"))
                    {
                        tx.Start();
                        try
                        {
                            WorkPointAlignmentService.Align(
                                document,
                                firstReference.ElementId,
                                secondReference.ElementId,
                                targetReference.ElementId);
                            tx.Commit();
                        }
                        catch
                        {
                            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                            throw;
                        }
                    }

                    alignedCount++;
                    SagaLog.Write(
                        $"AlignToWorkPointCommand: elemento {targetReference.ElementId.GetId()} " +
                        $"alinhado ao cruzamento de {firstReference.ElementId.GetId()}/{secondReference.ElementId.GetId()}.");
                }
                catch (Exception ex)
                {
                    SagaLog.Exception("AlignToWorkPointCommand.Align", ex);
                    TaskDialog.Show(
                        "SAGA - Alinhar ao ponto de trabalho",
                        $"Não foi possível alinhar este elemento:\n\n{ex.Message}\n\n" +
                        "Selecione outro trio ou pressione Esc para encerrar.");
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
