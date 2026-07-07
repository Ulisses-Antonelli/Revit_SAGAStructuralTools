using Autodesk.Revit.UI;
using SAGAStructuralTools.UI.ViewModels;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class StairWindow : Window
    {
        public StairWindow(UIApplication uiApp)
        {
            SagaLog.Write("StairWindow — InitializeComponent...");
            InitializeComponent();
            SagaLog.Write("StairWindow — criando ViewModel...");
            DataContext = new StairViewModel(uiApp);
            SagaLog.Write("StairWindow — construtor OK");
        }

        private void OnCancel(object sender, RoutedEventArgs e) => Close();
    }
}
