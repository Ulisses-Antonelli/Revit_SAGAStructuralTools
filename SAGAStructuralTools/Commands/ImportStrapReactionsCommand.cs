using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using SAGAStructuralTools.Revit.Strap;
using SAGAStructuralTools.Strap.Application;
using SAGAStructuralTools.Strap.Readers;
using SAGAStructuralTools.UI.Strap;
using SAGAStructuralTools.UI.ViewModels.Strap;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Interop;

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
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Selecionar relatório de reações STRAP",
                    Filter =
                        "Relatórios STRAP (*.txt;*.docx;*.rtf;*.pdf)|*.txt;*.docx;*.rtf;*.pdf|" +
                        "Todos os arquivos (*.*)|*.*",
                    Multiselect = false,
                    CheckFileExists = true
                };
                if (dialog.ShowDialog() != true)
                    return Result.Cancelled;

                IStrapDocumentReader reader =
                    new StrapDocumentReaderFactory().Create(dialog.FileName);
                DocumentReadResult read = reader.Read(dialog.FileName);
                if (read.IsBlocked || read.Document == null)
                {
                    TaskDialog.Show(
                        "Importar Reações STRAP",
                        string.Join(
                            Environment.NewLine,
                            read.Diagnostics.Select(item => item.Message)));
                    return Result.Cancelled;
                }

                StrapPartialImportResult import =
                    new StrapPartialImportService().Import(read.Document);
                Document document = commandData.Application.ActiveUIDocument.Document;
                var candidates = new StrapElementMapper().Collect(document);
                var viewModel = new ImportStrapReactionsViewModel(
                    dialog.FileName,
                    import,
                    candidates);
                var window = new ImportStrapReactionsWindow(viewModel);
                new WindowInteropHelper(window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;
                if (window.ShowDialog() != true || window.WritePlan == null)
                    return Result.Cancelled;

                new StrapReactionWriter().Write(document, window.WritePlan);
                TaskDialog.Show(
                    "Importar Reações STRAP",
                    $"{window.WritePlan.Items.Count} linha(s) importada(s) com sucesso.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("ImportStrapReactionsCommand", ex);
                message = ex.Message;
                TaskDialog.Show(
                    "Importar Reações STRAP",
                    "A importação foi cancelada e nenhuma linha foi gravada." +
                    Environment.NewLine + Environment.NewLine + ex.Message);
                return Result.Failed;
            }
        }
    }
}
