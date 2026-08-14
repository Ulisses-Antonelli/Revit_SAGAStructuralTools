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
    /// Interrompe uma viga em duas no ponto de interseção com o eixo de uma viga
    /// de referência. Fluxo: viga de referência, depois viga a dividir — dois
    /// cliques sequenciais, igual ao Arredondar Canto e ao Alinhar ao Ponto de Trabalho.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SplitBeamCommand : IExternalCommand
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
                SagaLog.Exception("SplitBeamCommand.Execute", ex);
                TaskDialog.Show(
                    "SAGA - Interromper viga",
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
                Reference targetReference;
                try
                {
                    string hint = splitCount > 0 ? $" ({splitCount} dividida(s); Esc encerra)" : " (Esc encerra)";
                    referenceReference = uiDocument.Selection.PickObject(
                        ObjectType.Element, filter,
                        "Selecione a viga de referência (ponto de corte)" + hint);
                    targetReference = uiDocument.Selection.PickObject(
                        ObjectType.Element, filter,
                        "Selecione a viga a dividir" + hint);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return splitCount > 0 ? Result.Succeeded : Result.Cancelled;
                }

                try
                {
                    using (var tx = new Transaction(document, "SAGA - Interromper viga"))
                    {
                        tx.Start();
                        try
                        {
                            SplitBeamService.Split(document, referenceReference.ElementId, targetReference.ElementId);
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
                        $"SplitBeamCommand: viga {targetReference.ElementId.GetId()} " +
                        $"dividida no cruzamento com {referenceReference.ElementId.GetId()}.");
                }
                catch (Exception ex)
                {
                    SagaLog.Exception("SplitBeamCommand.Split", ex);
                    TaskDialog.Show(
                        "SAGA - Interromper viga",
                        $"Não foi possível dividir esta viga:\n\n{ex.Message}\n\n" +
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
