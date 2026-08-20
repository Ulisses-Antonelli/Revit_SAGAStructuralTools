using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Stiffener;
using SAGAStructuralTools.UI.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class StiffenerWindow : Window
    {
        private static StiffenerWindow _current;

        internal static bool HasOpenWindow => _current != null;

        internal static bool TryActivateCurrent()
        {
            if (_current == null) return false;
            if (_current.WindowState == WindowState.Minimized)
                _current.WindowState = WindowState.Normal;
            _current.Activate();
            return true;
        }

        internal static bool TryRegister(StiffenerWindow window)
        {
            if (window == null || _current != null) return false;
            _current = window;
            window.Closed += OnRegisteredWindowClosed;
            return true;
        }

        internal static void Release(StiffenerWindow window)
        {
            if (!object.ReferenceEquals(_current, window)) return;
            window.Closed -= OnRegisteredWindowClosed;
            _current = null;
        }

        private static void OnRegisteredWindowClosed(object sender, System.EventArgs e)
        {
            Release(sender as StiffenerWindow);
        }

        public StiffenerWindow(ExternalEvent pickEvent, StiffenerPickHandler pickHandler,
                               ExternalEvent createEvent, StiffenerCreationHandler createHandler,
                               StiffenerEditContext editContext = null)
        {
            InitializeComponent();
            var viewModel = new StiffenerViewModel(pickEvent, pickHandler, createEvent, createHandler, editContext);
            DataContext = viewModel;
            Title = viewModel.WindowTitle;
            Closed += (s, e) => viewModel.Dispose();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is StiffenerViewModel vm) || vm.CanClose) Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is StiffenerViewModel vm && !vm.CanClose)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }
    }
}
