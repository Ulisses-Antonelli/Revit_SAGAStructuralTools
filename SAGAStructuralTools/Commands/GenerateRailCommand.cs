using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Rail;
using SAGAStructuralTools.UI;
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;

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
                var ownerHandle = Process.GetCurrentProcess().MainWindowHandle;

                // ExternalEvent.Create() deve ser chamado no thread da API do Revit.
                // Criamos aqui e passamos para o STA thread via closure.
                SagaLog.Write("Criando handlers e ExternalEvents no thread Revit...");
                var pickHandler   = new LinePickHandler();
                var pickEvent     = ExternalEvent.Create(pickHandler);
                var createHandler = new RailCreationHandler();
                var createEvent   = ExternalEvent.Create(createHandler);
                SagaLog.Write("ExternalEvents criados OK");

                // A janela corre em STA thread dedicado para isolar seu contexto
                // de composição WPF/D3D do pipeline de rendering do Revit, que
                // causa crash 0xe0434352 quando compartilhado no mesmo thread.
                SagaLog.Write("Iniciando STA thread para RailWindow...");
                var thread = new Thread(() =>
                {
                    SagaLog.Write("STA thread iniciado — criando RailWindow...");
                    try
                    {
                        var window = new RailWindow(pickEvent, pickHandler, createEvent, createHandler);
                        new WindowInteropHelper(window).Owner = ownerHandle;
                        window.Closed += (s, e) => Dispatcher.CurrentDispatcher.InvokeShutdown();
                        SagaLog.Write("STA thread — chamando window.Show()...");
                        window.Show();
                        SagaLog.Write("STA thread — window.Show() OK, iniciando Dispatcher.Run()");
                        Dispatcher.Run();
                        SagaLog.Write("STA thread — encerrado");
                    }
                    catch (Exception ex)
                    {
                        SagaLog.Exception("STA thread RailWindow", ex);
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "SAGA-RailWindow";
                thread.Start();

                SagaLog.Write("Execute retornando Succeeded (STA thread iniciado)");
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
