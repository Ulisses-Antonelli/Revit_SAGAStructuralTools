using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Commands.Strap;
using System;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ImportStrapReactionsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            // Deve ser a primeira operacao: SagaLog depende somente da BCL.
            SagaLog.Write("ImportStrapReactionsCommand.Execute: entrada");

            string failureMessage = null;
            Result result = SafeExecutionBoundary.Run(
                () => StrapImportExecution.ExecuteCore(commandData, elements),
                exception =>
                {
                    SagaLog.Exception("ImportStrapReactionsCommand.Execute", exception);
                    failureMessage = exception.Message;
                    ShowFailureSafely(exception.Message);
                    return Result.Failed;
                });

            if (!string.IsNullOrEmpty(failureMessage))
                message = failureMessage;
            return result;
        }

        private static void ShowFailureSafely(string detail)
        {
            try
            {
                TaskDialog.Show(
                    "Importar Reacoes STRAP",
                    "A importacao foi cancelada e nenhuma linha foi gravada." +
                    Environment.NewLine + Environment.NewLine + detail);
            }
            catch (Exception dialogException)
            {
                SagaLog.Exception("ImportStrapReactionsCommand.TaskDialog", dialogException);
            }
        }
    }
}
