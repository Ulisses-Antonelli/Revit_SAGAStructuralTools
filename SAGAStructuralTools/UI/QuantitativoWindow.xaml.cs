using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Quantitativo;
using SAGAStructuralTools.UI.ViewModels;
using System.Collections.Generic;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class QuantitativoWindow : Window
    {
        private static QuantitativoWindow _current;

        internal static bool HasOpenWindow => _current != null;

        internal static bool TryActivateCurrent()
        {
            if (_current == null) return false;
            if (_current.WindowState == WindowState.Minimized)
                _current.WindowState = WindowState.Normal;
            _current.Activate();
            return true;
        }

        internal static bool TryRegister(QuantitativoWindow window)
        {
            if (window == null || _current != null) return false;
            _current = window;
            window.Closed += OnRegisteredWindowClosed;
            return true;
        }

        internal static void Release(QuantitativoWindow window)
        {
            if (!object.ReferenceEquals(_current, window)) return;
            window.Closed -= OnRegisteredWindowClosed;
            _current = null;
        }

        private static void OnRegisteredWindowClosed(object sender, System.EventArgs e)
        {
            Release(sender as QuantitativoWindow);
        }

        public QuantitativoWindow(List<ElementId> selectedIds, List<QuantitativoMeasurement> measurements,
                                  IEnumerable<string> warnings, int skippedCount,
                                  ExternalEvent scheduleEvent, QuantitativoScheduleHandler scheduleHandler)
        {
            InitializeComponent();
            DataContext = new QuantitativoViewModel(
                selectedIds, measurements, warnings, skippedCount, scheduleEvent, scheduleHandler);
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
