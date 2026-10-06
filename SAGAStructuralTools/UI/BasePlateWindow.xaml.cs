using SAGAStructuralTools.UI.ViewModels;
using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Threading;

namespace SAGAStructuralTools.UI
{
    public partial class BasePlateWindow : Window
    {
        public BasePlateWindow()
            : this(null)
        {
        }

        public BasePlateWindow(UIDocument uiDocument)
        {
            try
            {
                InitializeComponent();
                Dispatcher.UnhandledException += OnDispatcherUnhandledException;
                DataContext = new BasePlateViewModel(uiDocument);
            }
            catch (Exception ex)
            {
                SagaLog.Exception("BasePlateWindow.Constructor", ex);
                MessageBox.Show(
                    "Não foi possível abrir a ferramenta de placa de base.\n\n" +
                    ex.Message,
                    "Dimensionamento de Placa de Base",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                throw;
            }
        }

        private void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            SagaLog.Exception("BasePlateWindow.DispatcherUnhandledException", e.Exception);
            MessageBox.Show(
                "Ocorreu um erro na interface da ferramenta.\n\n" +
                e.Exception.Message,
                "Dimensionamento de Placa de Base",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
