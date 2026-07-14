using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Rail;
using SAGAStructuralTools.UI.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class RailWindow : Window
    {
        private static RailWindow _current;

        internal static bool HasOpenWindow => _current != null;

        internal static bool TryActivateCurrent()
        {
            if (_current == null) return false;
            if (_current.WindowState == WindowState.Minimized)
                _current.WindowState = WindowState.Normal;
            _current.Activate();
            return true;
        }

        internal static bool TryRegister(RailWindow window)
        {
            if (window == null || _current != null) return false;
            _current = window;
            window.Closed += OnRegisteredWindowClosed;
            return true;
        }

        internal static void Release(RailWindow window)
        {
            if (!object.ReferenceEquals(_current, window)) return;
            window.Closed -= OnRegisteredWindowClosed;
            _current = null;
        }

        private static void OnRegisteredWindowClosed(object sender, System.EventArgs e)
        {
            Release(sender as RailWindow);
        }

        public RailWindow(ExternalEvent pickEvent, LinePickHandler pickHandler,
                          ExternalEvent createEvent, RailCreationHandler createHandler,
                          RailEditContext editContext = null)
        {
            SagaLog.Write("RailWindow — InitializeComponent...");
            InitializeComponent();
            SagaLog.Write("RailWindow — InitializeComponent OK");
            var viewModel = new RailViewModel(
                pickEvent, pickHandler, createEvent, createHandler, editContext);
            DataContext = viewModel;
            Title = viewModel.WindowTitle;
            SagaLog.Write("RailWindow — DataContext setado, construtor OK");
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is RailViewModel vm) || vm.CanClose) Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is RailViewModel vm && !vm.CanClose)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }
    }
}
