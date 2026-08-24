using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.EndPlate;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateEndPlateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            ExternalEvent pickSingleEvent = null;
            ExternalEvent pickTwoEvent = null;
            ExternalEvent createEvent = null;
            EndPlateWindow window = null;
            try
            {
                if (EndPlateWindow.TryActivateCurrent())
                    return Result.Succeeded;

                var pickSingleHandler = new EndPlatePickHandler();
                pickSingleEvent       = ExternalEvent.Create(pickSingleHandler);
                var pickTwoHandler    = new EndPlateTwoPickHandler();
                pickTwoEvent          = ExternalEvent.Create(pickTwoHandler);
                var createHandler     = new EndPlateCreationHandler();
                createEvent           = ExternalEvent.Create(createHandler);

                window = new EndPlateWindow(
                    pickSingleEvent, pickSingleHandler, pickTwoEvent, pickTwoHandler, createEvent, createHandler);
                if (!EndPlateWindow.TryRegister(window))
                {
                    pickSingleEvent.Dispose();
                    pickTwoEvent.Dispose();
                    createEvent.Dispose();
                    EndPlateWindow.TryActivateCurrent();
                    return Result.Succeeded;
                }

                var ownedPickSingleEvent = pickSingleEvent;
                var ownedPickTwoEvent = pickTwoEvent;
                var ownedCreateEvent = createEvent;
                window.Closed += (sender, args) =>
                {
                    ownedPickSingleEvent.Dispose();
                    ownedPickTwoEvent.Dispose();
                    ownedCreateEvent.Dispose();
                };
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                EndPlateWindow.Release(window);
                pickSingleEvent?.Dispose();
                pickTwoEvent?.Dispose();
                createEvent?.Dispose();
                SagaLog.Exception("GenerateEndPlateCommand.Execute", ex);
                message = $"Erro interno ao abrir janela de chapa de topo: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}
