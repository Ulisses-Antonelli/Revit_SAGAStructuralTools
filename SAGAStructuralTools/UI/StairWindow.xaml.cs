using Autodesk.Revit.UI;
using SAGAStructuralTools.UI.ViewModels;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class StairWindow : Window
    {
        public StairWindow(UIApplication uiApp)
        {
            InitializeComponent();
            DataContext = new StairViewModel(uiApp);
        }
    }
}
