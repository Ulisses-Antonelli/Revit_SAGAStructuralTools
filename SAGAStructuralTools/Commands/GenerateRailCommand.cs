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
            ExternalEvent pickEvent = null;
            ExternalEvent createEvent = null;
            RailWindow window = null;
            try
            {
                if (RailWindow.TryActivateCurrent())
                {
                    SagaLog.Write("RailWindow já estava aberta; janela existente ativada.");
                    return Result.Succeeded;
                }

                // Seleção via PickObject único (uma linha por clique) e criação via
                // ExternalEvent — ambos rodam no contexto de API do Revit. Criados aqui,
                // no thread da API, e passados para a janela.
                var pickHandler   = new LinePickHandler();
                pickEvent         = ExternalEvent.Create(pickHandler);
                var createHandler = new RailCreationHandler();
                createEvent       = ExternalEvent.Create(createHandler);

                window = new RailWindow(pickEvent, pickHandler, createEvent, createHandler);
                if (!RailWindow.TryRegister(window))
                {
                    pickEvent.Dispose();
                    createEvent.Dispose();
                    RailWindow.TryActivateCurrent();
                    return Result.Succeeded;
                }

                window.Closed += (sender, args) =>
                {
                    pickEvent.Dispose();
                    createEvent.Dispose();
                };
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                window.Show();

                SagaLog.Write("RailWindow exibida — Execute retornando Succeeded");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                RailWindow.Release(window);
                pickEvent?.Dispose();
                createEvent?.Dispose();
                SagaLog.Exception("GenerateRailCommand.Execute", ex);
                message = $"Erro interno ao abrir janela de guarda-corpo: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}
