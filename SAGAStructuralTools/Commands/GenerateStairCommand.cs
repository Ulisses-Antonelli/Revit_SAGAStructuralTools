using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.UI;
using System.Diagnostics;
using System.Windows.Interop;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateStairCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var window = new StairWindow(commandData.Application);

            // Janela não-modal (Show, não ShowDialog) para que o thread do Revit
            // continue rodando e o ExternalEvent de seleção de vigas possa ser executado.
            // WindowInteropHelper define o Revit como janela pai.
            new WindowInteropHelper(window).Owner =
                Process.GetCurrentProcess().MainWindowHandle;

            window.Show();
            return Result.Succeeded;
        }
    }
}
