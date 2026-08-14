using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Ladder;
using SAGAStructuralTools.UI.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class LadderWindow : Window
    {
        private static LadderWindow _current;

        internal static bool HasOpenWindow => _current != null;

        internal static bool TryActivateCurrent()
        {
            if (_current == null) return false;
            if (_current.WindowState == WindowState.Minimized)
                _current.WindowState = WindowState.Normal;
            _current.Activate();
            return true;
        }

        internal static bool TryRegister(LadderWindow window)
        {
            if (window == null || _current != null) return false;
            _current = window;
            window.Closed += OnRegisteredWindowClosed;
            return true;
        }

        internal static void Release(LadderWindow window)
        {
            if (!object.ReferenceEquals(_current, window)) return;
            window.Closed -= OnRegisteredWindowClosed;
            _current = null;
        }

        private static void OnRegisteredWindowClosed(object sender, System.EventArgs e)
        {
            Release(sender as LadderWindow);
        }

        public LadderWindow(ExternalEvent pickEvent, LadderPickHandler pickHandler,
                            ExternalEvent createEvent, LadderCreationHandler createHandler,
                            LadderEditContext editContext = null)
        {
            SagaLog.Write("LadderWindow — InitializeComponent...");
            InitializeComponent();
            SagaLog.Write("LadderWindow — InitializeComponent OK");
            var viewModel = new LadderViewModel(
                pickEvent, pickHandler, createEvent, createHandler, editContext);
            DataContext = viewModel;
            Title = viewModel.WindowTitle;
            SagaLog.Write("LadderWindow — DataContext setado, construtor OK");
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is LadderViewModel vm) || vm.CanClose) Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is LadderViewModel vm && !vm.CanClose)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }
    }
}
