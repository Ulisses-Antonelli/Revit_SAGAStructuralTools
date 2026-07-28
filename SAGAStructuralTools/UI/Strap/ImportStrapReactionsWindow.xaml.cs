using SAGAStructuralTools.Revit.Strap;
using SAGAStructuralTools.UI.ViewModels.Strap;
using System.Windows;

namespace SAGAStructuralTools.UI.Strap
{
    public partial class ImportStrapReactionsWindow : Window
    {
        private readonly ImportStrapReactionsViewModel _viewModel;

        internal ImportStrapReactionsWindow(
            ImportStrapReactionsViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        internal ReactionWritePlan WritePlan { get; private set; }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanConfirm)
                return;
            WritePlan = _viewModel.CreatePlan();
            DialogResult = true;
        }
    }
}
