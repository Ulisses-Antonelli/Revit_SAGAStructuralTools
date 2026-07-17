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
    /// Alinhamento real: estende o eixo verdadeiro de um componente (LocationCurve, ou
    /// nível/offset em pilares por ponto) até uma referência — diferente do Alinhar (AL)
    /// nativo do Revit, que em membros juntados só ajusta o recuo visual da junta sem
    /// mover o eixo real.
    /// Fluxo: seleciona primeiro a referência (face, linha, ou eixo de outro membro),
    /// depois o componente a estender — igual à ordem do Alinhar nativo.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RealAlignCommand : IExternalCommand
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
                SagaLog.Exception("RealAlignCommand.Execute", ex);
                TaskDialog.Show(
                    "SAGA - Alinhamento real",
                    $"A ferramenta foi encerrada por um erro inesperado:\n\n{ex.Message}");
                return Result.Cancelled;
            }
        }

        private static Result RunContinuous(UIDocument uiDocument, Document document)
        {
            var componentFilter = new ExtendableComponentFilter();
            int alignedCount = 0;

            while (true)
            {
                Reference targetReference;
                Reference sourceReference;
                try
                {
                    string hint = alignedCount > 0 ? $" ({alignedCount} alinhado(s); Esc encerra)" : " (Esc encerra)";
                    targetReference = uiDocument.Selection.PickObject(
                        ObjectType.Element,
                        "Selecione a referência (face, linha, ou eixo de outro componente)" + hint);
                    sourceReference = uiDocument.Selection.PickObject(
                        ObjectType.Element,
                        componentFilter,
                        "Selecione o componente a estender até a referência" + hint);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return alignedCount > 0 ? Result.Succeeded : Result.Cancelled;
                }

                try
                {
                    if (targetReference.ElementId.Equals(sourceReference.ElementId))
                        throw new InvalidOperationException("Selecione a referência e o componente separadamente.");

                    using (var tx = new Transaction(document, "SAGA - Alinhamento real"))
                    {
                        tx.Start();
                        try
                        {
                            RealAlignmentService.Align(document, targetReference.ElementId, sourceReference.ElementId);
                            tx.Commit();
                        }
                        catch
                        {
                            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                            throw;
                        }
                    }

                    alignedCount++;
                    SagaLog.Write($"RealAlignCommand: componente {sourceReference.ElementId.GetId()} " +
                                  $"estendido até referência {targetReference.ElementId.GetId()}.");
                }
                catch (Exception ex)
                {
                    SagaLog.Exception("RealAlignCommand.Align", ex);
                    TaskDialog.Show(
                        "SAGA - Alinhamento real",
                        $"Não foi possível alinhar este componente:\n\n{ex.Message}\n\n" +
                        "Selecione outro par ou pressione Esc para encerrar.");
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
