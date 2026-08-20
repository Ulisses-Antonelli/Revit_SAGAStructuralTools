using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Stair;
using SAGAStructuralTools.UI.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class StairWindow : Window
    {
        private static StairWindow _current;

        internal static bool HasOpenWindow => _current != null;

        internal static bool TryActivateCurrent()
        {
            if (_current == null) return false;
            if (_current.WindowState == WindowState.Minimized)
                _current.WindowState = WindowState.Normal;
            _current.Activate();
            return true;
        }

        internal static bool TryRegister(StairWindow window)
        {
            if (window == null || _current != null) return false;
            _current = window;
            window.Closed += OnRegisteredWindowClosed;
            return true;
        }

        internal static void Release(StairWindow window)
        {
            if (!object.ReferenceEquals(_current, window)) return;
            window.Closed -= OnRegisteredWindowClosed;
            _current = null;
        }

        private static void OnRegisteredWindowClosed(object sender, System.EventArgs e)
        {
            Release(sender as StairWindow);
        }

        public StairWindow(UIApplication uiApp, StairEditContext editContext = null)
        {
            SagaLog.Write("StairWindow — InitializeComponent...");
            InitializeComponent();
            SagaLog.Write("StairWindow — criando ViewModel...");
            var viewModel = new StairViewModel(uiApp, editContext);
            DataContext = viewModel;
            Title = viewModel.WindowTitle;
            SagaLog.Write("StairWindow — construtor OK");
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is StairViewModel vm) || vm.CanClose) Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is StairViewModel vm && !vm.CanClose)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }
    }
}
