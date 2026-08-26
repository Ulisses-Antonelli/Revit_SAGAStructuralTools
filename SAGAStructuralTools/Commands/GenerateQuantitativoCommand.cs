using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Quantitativo;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateQuantitativoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            ExternalEvent scheduleEvent = null;
            QuantitativoWindow window = null;
            try
            {
                if (QuantitativoWindow.TryActivateCurrent())
                    return Result.Succeeded;

                var uidoc = commandData.Application.ActiveUIDocument;
                var selectedIds = uidoc.Selection.GetElementIds().ToList();
                if (selectedIds.Count == 0)
                {
                    message = "Selecione uma ou mais vigas/pilares de aço antes de rodar o comando.";
                    return Result.Failed;
                }

                var collected = QuantitativoCollector.Collect(uidoc.Document, selectedIds);
                if (collected.Entries.Count == 0)
                {
                    message = "Nenhuma viga/pilar de aço válido na seleção (chapas e elementos de concreto são ignorados).";
                    return Result.Failed;
                }

                var scheduleHandler = new QuantitativoScheduleHandler();
                scheduleEvent = ExternalEvent.Create(scheduleHandler);

                window = new QuantitativoWindow(
                    selectedIds, collected.Measurements, collected.Warnings, collected.SkippedNonSteelCount,
                    scheduleEvent, scheduleHandler);

                if (!QuantitativoWindow.TryRegister(window))
                {
                    scheduleEvent.Dispose();
                    QuantitativoWindow.TryActivateCurrent();
                    return Result.Succeeded;
                }

                var ownedScheduleEvent = scheduleEvent;
                window.Closed += (sender, args) => ownedScheduleEvent.Dispose();
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                QuantitativoWindow.Release(window);
                scheduleEvent?.Dispose();
                SagaLog.Exception("GenerateQuantitativoCommand.Execute", ex);
                message = $"Erro interno ao abrir o quantitativo: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}
