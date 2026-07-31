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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif

namespace SAGAStructuralTools.Commands.Strap
{
    internal static class StrapImportExecution
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Result ExecuteCore(
            ExternalCommandData commandData,
            ElementSet elements)
        {
            LogRuntime(commandData);

            var dialog = new OpenFileDialog
            {
                Title = "Selecionar relatorio de reacoes STRAP",
                Filter =
                    "Relatorios STRAP (*.txt;*.docx;*.rtf;*.pdf)|*.txt;*.docx;*.rtf;*.pdf|" +
                    "Documento Word legado (*.doc)|*.doc|Todos os arquivos (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != true)
            {
                SagaLog.Write("Selecao de arquivo cancelada pelo usuario.");
                return Result.Cancelled;
            }

            string filePath = dialog.FileName;
            string extension = Path.GetExtension(filePath) ?? string.Empty;
            SagaLog.Write("Arquivo selecionado: " + filePath);
            SagaLog.Write("Extensao selecionada: " + extension);

            if (StrapFileSelection.Classify(filePath) == StrapFileSelectionKind.LegacyDoc)
            {
                SagaLog.Write("Arquivo .doc legado rejeitado antes da factory.");
                TaskDialog.Show(
                    "Importar Reacoes STRAP",
                    "Arquivos .doc legados nao sao suportados. Converta o arquivo para " +
                    ".docx, .rtf, .txt ou PDF textual e tente novamente.");
                return Result.Cancelled;
            }

            LogThreadAndAssemblies("antes de StrapDocumentReaderFactory");
            SagaLog.Write("Antes de criar StrapDocumentReaderFactory.");
            var factory = new StrapDocumentReaderFactory();
            IStrapDocumentReader reader = factory.Create(filePath);
            SagaLog.Write("Reader selecionado: " + reader.GetType().FullName);
            LogAssembly("Assembly do reader", reader.GetType().Assembly);

            SagaLog.Write("Antes de Read().");
            DocumentReadResult read;
            try
            {
                read = reader.Read(filePath);
                SagaLog.Write("Depois de Read(): bloqueado=" + read.IsBlocked +
                    ", documento=" + (read.Document == null ? "nulo" : "presente") +
                    ", diagnosticos=" + read.Diagnostics.Count);
            }
            catch (Exception exception)
            {
                SagaLog.Exception("StrapDocumentReader.Read", exception);
                throw;
            }

            if (read.IsBlocked || read.Document == null)
            {
                TaskDialog.Show(
                    "Importar Reacoes STRAP",
                    string.Join(Environment.NewLine,
                        read.Diagnostics.Select(item => item.Message)));
                return Result.Cancelled;
            }

            SagaLog.Write("Antes do parser/Application.");
            StrapPartialImportResult import;
            try
            {
                import = new StrapPartialImportService().Import(read.Document);
                SagaLog.Write("Depois do parser/Application: bloqueado=" +
                    import.IsGloballyBlocked + ", nos=" + import.Nodes.Count +
                    ", validos=" + import.ValidNodeCount +
                    ", invalidos=" + import.InvalidNodeCount);
            }
            catch (Exception exception)
            {
                SagaLog.Exception("StrapPartialImportService.Import", exception);
                throw;
            }

            Document document = commandData.Application.ActiveUIDocument.Document;
            var candidates = new StrapElementMapper().Collect(document);
            var viewModel = new ImportStrapReactionsViewModel(filePath, import, candidates);
            LogWindowRuntime();
            SagaLog.Write("Antes do construtor da janela de previa.");
            var window = new ImportStrapReactionsWindow(viewModel);
            SagaLog.Write("Depois do construtor da janela de previa.");
            new WindowInteropHelper(window).Owner = Process.GetCurrentProcess().MainWindowHandle;
            window.Closing += (sender, args) =>
                SagaLog.Write("Inicio do fechamento da janela de previa.");
            window.Closed += (sender, args) =>
                SagaLog.Write("Termino do fechamento da janela de previa.");

            DispatcherUnhandledExceptionEventHandler diagnosticHandler =
                (sender, args) =>
                {
                    SagaLog.Exception(
                        "ImportStrapReactionsWindow.DispatcherUnhandledException",
                        args.Exception);
                    // Diagnostico somente: nao marcar Handled nem tentar continuar.
                };
            window.Dispatcher.UnhandledException += diagnosticHandler;
            bool? dialogResult;
            try
            {
                SagaLog.Write("Imediatamente antes de ShowDialog().");
                dialogResult = window.ShowDialog();
                SagaLog.Write("Retorno de ShowDialog(): " +
                    (dialogResult.HasValue ? dialogResult.Value.ToString() : "null"));
            }
            finally
            {
                try
                {
                    window.Dispatcher.UnhandledException -= diagnosticHandler;
                    SagaLog.Write("Handler diagnostico do Dispatcher removido.");
                }
                catch (Exception exception)
                {
                    SagaLog.Exception("Remocao do handler do Dispatcher", exception);
                }
            }
            if (dialogResult != true || window.WritePlan == null)
            {
                SagaLog.Write("Janela de previa cancelada.");
                return Result.Cancelled;
            }

            new StrapReactionWriter().Write(document, window.WritePlan);
            TaskDialog.Show(
                "Importar Reacoes STRAP",
                window.WritePlan.Items.Count + " linha(s) importada(s) com sucesso.");
            return Result.Succeeded;
        }

        private static void LogRuntime(ExternalCommandData commandData)
        {
            try
            {
                var application = commandData.Application.Application;
                SagaLog.Write("Revit: " + application.VersionName +
                    " (" + application.VersionNumber + ")");
            }
            catch (Exception exception)
            {
                SagaLog.Exception("Diagnostico da versao do Revit", exception);
            }
            LogThreadAndAssemblies("entrada de ExecuteCore");
        }

        private static void LogThreadAndAssemblies(string stage)
        {
            try
            {
                Thread thread = Thread.CurrentThread;
                SagaLog.Write(stage + ": thread=" + thread.ManagedThreadId +
                    ", state=" + thread.ThreadState +
                    ", apartment=" + thread.GetApartmentState());
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()
                    .Where(IsDiagnosticAssembly)
                    .OrderBy(item => item.GetName().Name))
                {
                    LogAssembly(stage, assembly);
                }
            }
            catch (Exception exception)
            {
                SagaLog.Exception("Diagnostico de thread/assemblies", exception);
            }
        }

        private static void LogWindowRuntime()
        {
            try
            {
                Thread thread = Thread.CurrentThread;
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                SagaLog.Write("Runtime da janela: thread=" + thread.ManagedThreadId +
                    ", state=" + thread.ThreadState +
                    ", apartment=" + thread.GetApartmentState() +
                    ", dispatcherThread=" + dispatcher.Thread.ManagedThreadId +
                    ", dispatcherShutdownStarted=" + dispatcher.HasShutdownStarted +
                    ", dispatcherShutdownFinished=" + dispatcher.HasShutdownFinished);
                LogAssembly("DLL principal", Assembly.GetExecutingAssembly());
                LogAssembly("Domain", typeof(global::SAGAStructuralTools.Strap.Domain.ConsolidatedReaction).Assembly);
                LogAssembly("Application", typeof(StrapPartialImportService).Assembly);
                LogAssembly("Readers", typeof(StrapDocumentReaderFactory).Assembly);
            }
            catch (Exception exception)
            {
                SagaLog.Exception("Diagnostico do runtime da janela", exception);
            }
        }

        private static bool IsDiagnosticAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name ?? string.Empty;
            return name.StartsWith("SAGAStructuralTools.Strap.Readers", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("DocumentFormat.OpenXml", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("UglyToad.PdfPig", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("PresentationFramework", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("System.Xaml", StringComparison.OrdinalIgnoreCase);
        }

        private static void LogAssembly(string stage, Assembly assembly)
        {
            try
            {
                string location;
                try { location = assembly.Location; }
                catch { location = "<indisponivel>"; }
                string context = "<nao aplicavel>";
#if NET8_0_OR_GREATER
                AssemblyLoadContext loadContext = AssemblyLoadContext.GetLoadContext(assembly);
                context = loadContext == null ? "<nulo>" : (loadContext.Name ?? "<sem nome>");
#endif
                SagaLog.Write(stage + ": assembly=" + assembly.FullName +
                    ", location=" + location + ", loadContext=" + context);
            }
            catch (Exception exception)
            {
                SagaLog.Exception("Diagnostico de assembly", exception);
            }
        }
    }
}
