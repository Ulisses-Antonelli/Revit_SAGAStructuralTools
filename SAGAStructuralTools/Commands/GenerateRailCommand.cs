using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Rail;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateRailCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            SagaLog.Write("=== GenerateRailCommand.Execute iniciado ===");
            try
            {
                // Seleção via PickObject único (uma linha por clique) e criação via
                // ExternalEvent — ambos rodam no contexto de API do Revit. Criados aqui,
                // no thread da API, e passados para a janela.
                var pickHandler   = new LinePickHandler();
                var pickEvent     = ExternalEvent.Create(pickHandler);
                var createHandler = new RailCreationHandler();
                var createEvent   = ExternalEvent.Create(createHandler);

                var window = new RailWindow(pickEvent, pickHandler, createEvent, createHandler);
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                window.Show();

                SagaLog.Write("RailWindow exibida — Execute retornando Succeeded");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("GenerateRailCommand.Execute", ex);
                message = $"Erro interno ao abrir janela de guarda-corpo: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}
