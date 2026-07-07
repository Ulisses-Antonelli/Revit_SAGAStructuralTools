using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Rail;
using SAGAStructuralTools.UI.ViewModels;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class RailWindow : Window
    {
        public RailWindow(ExternalEvent pickEvent, LinePickHandler pickHandler,
                          ExternalEvent createEvent, RailCreationHandler createHandler)
        {
            SagaLog.Write("RailWindow — InitializeComponent...");
            InitializeComponent();
            SagaLog.Write("RailWindow — InitializeComponent OK");
            SourceInitialized += (s, e) => SagaLog.Write("RailWindow — SourceInitialized (HWND criado)");
            ContentRendered   += (s, e) => SagaLog.Write("RailWindow — ContentRendered (primeiro frame)");
            Loaded            += (s, e) => SagaLog.Write("RailWindow — Loaded");
            SagaLog.Write("RailWindow — criando ViewModel...");
            DataContext = new RailViewModel(pickEvent, pickHandler, createEvent, createHandler);
            SagaLog.Write("RailWindow — DataContext setado, construtor OK");
        }

        private void OnCancel(object sender, RoutedEventArgs e) => Close();
    }
}
