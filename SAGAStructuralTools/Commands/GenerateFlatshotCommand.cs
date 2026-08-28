using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Flatshot;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateFlatshotCommand : IExternalCommand
    {
        private static readonly ViewType[] UnsupportedViewTypes =
        {
            ViewType.Schedule, ViewType.DrawingSheet, ViewType.Legend,
            ViewType.DraftingView, ViewType.Undefined, ViewType.ProjectBrowser,
            ViewType.SystemBrowser, ViewType.Internal
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;
            var sourceView = uidoc.ActiveView;

            if (sourceView == null || Array.IndexOf(UnsupportedViewTypes, sourceView.ViewType) >= 0)
            {
                message = "Ative uma vista de planta, corte, elevação, detalhe ou 3D antes de rodar este comando.";
                return Result.Failed;
            }

            string defaultName = $"REGISTRO - {sourceView.Name} - {DateTime.Now:yyyy-MM-dd HHmm}";
            var dialog = new FlatshotWindow(defaultName);
            new WindowInteropHelper(dialog).Owner = Process.GetCurrentProcess().MainWindowHandle;
            if (dialog.ShowDialog() != true)
                return Result.Cancelled;

            try
            {
                FlatshotService.FlatshotResult result;
                using (var tx = new Transaction(doc, "SAGA - Registro (Flatshot)"))
                {
                    tx.Start();
                    result = FlatshotService.Create(doc, sourceView, dialog.ViewName);
                    tx.Commit();
                }

                if (dialog.OpenAfterCreate && doc.GetElement(result.ViewId) is View newView)
                    uidoc.ActiveView = newView;

                TaskDialog.Show(
                    "SAGA - Registro (Flatshot)",
                    $"Vista '{result.ViewName}' criada com sucesso — geometria e cotas congeladas, " +
                    "independentes de mudanças futuras no modelo.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("GenerateFlatshotCommand.Execute", ex);
                message = $"Erro ao gerar o registro: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}
