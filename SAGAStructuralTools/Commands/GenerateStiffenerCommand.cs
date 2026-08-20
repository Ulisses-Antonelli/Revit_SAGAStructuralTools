using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Stiffener;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateStiffenerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            ExternalEvent pickEvent = null;
            ExternalEvent createEvent = null;
            StiffenerWindow window = null;
            try
            {
                if (StiffenerWindow.TryActivateCurrent())
                    return Result.Succeeded;

                var pickHandler   = new StiffenerPickHandler();
                pickEvent         = ExternalEvent.Create(pickHandler);
                var createHandler = new StiffenerCreationHandler();
                createEvent       = ExternalEvent.Create(createHandler);

                window = new StiffenerWindow(pickEvent, pickHandler, createEvent, createHandler);
                if (!StiffenerWindow.TryRegister(window))
                {
                    pickEvent.Dispose();
                    createEvent.Dispose();
                    StiffenerWindow.TryActivateCurrent();
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

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StiffenerWindow.Release(window);
                pickEvent?.Dispose();
                createEvent?.Dispose();
                SagaLog.Exception("GenerateStiffenerCommand.Execute", ex);
                message = $"Erro interno ao abrir janela de nervura: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}
