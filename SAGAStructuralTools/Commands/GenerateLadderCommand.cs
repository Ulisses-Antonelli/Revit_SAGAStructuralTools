using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Ladder;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateLadderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            SagaLog.Write("=== GenerateLadderCommand.Execute iniciado ===");
            ExternalEvent pickEvent = null;
            ExternalEvent createEvent = null;
            LadderWindow window = null;
            try
            {
                if (LadderWindow.TryActivateCurrent())
                {
                    SagaLog.Write("LadderWindow já estava aberta; janela existente ativada.");
                    return Result.Succeeded;
                }

                var pickHandler   = new LadderPickHandler();
                pickEvent         = ExternalEvent.Create(pickHandler);
                var createHandler = new LadderCreationHandler();
                createEvent       = ExternalEvent.Create(createHandler);

                window = new LadderWindow(pickEvent, pickHandler, createEvent, createHandler);
                if (!LadderWindow.TryRegister(window))
                {
                    pickEvent.Dispose();
                    createEvent.Dispose();
                    LadderWindow.TryActivateCurrent();
                    return Result.Succeeded;
                }

                var ownedPickEvent = pickEvent;
                var ownedCreateEvent = createEvent;
                window.Closed += (sender, args) =>
                {
                    ownedPickEvent.Dispose();
                    ownedCreateEvent.Dispose();
                };
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                window.Show();

                SagaLog.Write("LadderWindow exibida — Execute retornando Succeeded");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                LadderWindow.Release(window);
                pickEvent?.Dispose();
                createEvent?.Dispose();
                SagaLog.Exception("GenerateLadderCommand.Execute", ex);
                message = $"Erro interno ao abrir janela de escada marinheiro: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}
