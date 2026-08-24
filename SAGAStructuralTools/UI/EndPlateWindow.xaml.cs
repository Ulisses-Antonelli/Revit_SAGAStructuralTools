using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.EndPlate;
using SAGAStructuralTools.UI.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class EndPlateWindow : Window
    {
        private static EndPlateWindow _current;

        internal static bool HasOpenWindow => _current != null;

        internal static bool TryActivateCurrent()
        {
            if (_current == null) return false;
            if (_current.WindowState == WindowState.Minimized)
                _current.WindowState = WindowState.Normal;
            _current.Activate();
            return true;
        }

        internal static bool TryRegister(EndPlateWindow window)
        {
            if (window == null || _current != null) return false;
            _current = window;
            window.Closed += OnRegisteredWindowClosed;
            return true;
        }

        internal static void Release(EndPlateWindow window)
        {
            if (!object.ReferenceEquals(_current, window)) return;
            window.Closed -= OnRegisteredWindowClosed;
            _current = null;
        }

        private static void OnRegisteredWindowClosed(object sender, System.EventArgs e)
        {
            Release(sender as EndPlateWindow);
        }

        public EndPlateWindow(ExternalEvent pickSingleEvent, EndPlatePickHandler pickSingleHandler,
                              ExternalEvent pickTwoEvent, EndPlateTwoPickHandler pickTwoHandler,
                              ExternalEvent createEvent, EndPlateCreationHandler createHandler,
                              EndPlateEditContext editContext = null)
        {
            InitializeComponent();
            var viewModel = new EndPlateViewModel(
                pickSingleEvent, pickSingleHandler, pickTwoEvent, pickTwoHandler, createEvent, createHandler, editContext);
            DataContext = viewModel;
            Title = viewModel.WindowTitle;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is EndPlateViewModel vm) || vm.CanClose) Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is EndPlateViewModel vm && !vm.CanClose)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }
    }
}
