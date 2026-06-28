using Autodesk.Revit.UI;
using SAGAStructuralTools.UI.ViewModels;
using System.Collections.Specialized;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow(UIApplication uiApp)
        {
            InitializeComponent();
            var vm = new MainViewModel(uiApp);
            DataContext = vm;

            // Auto-scroll do log para a última entrada adicionada
            vm.LogEntries.CollectionChanged += OnLogChanged;
        }

        private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (LogListBox.Items.Count > 0)
                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
        }
    }
}
