using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateStairCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            SagaLog.Write("=== GenerateStairCommand.Execute iniciado ===");
            try
            {
                SagaLog.Write("Criando StairWindow...");
                var window = new StairWindow(commandData.Application);
                SagaLog.Write("StairWindow criada — definindo owner...");
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                window.Show();
                SagaLog.Write("StairWindow exibida — Execute retornando Succeeded");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("GenerateStairCommand.Execute", ex);
                message = $"Erro interno ao abrir janela de escada: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}
