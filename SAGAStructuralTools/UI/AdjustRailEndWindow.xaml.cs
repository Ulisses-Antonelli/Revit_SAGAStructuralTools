using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace SAGAStructuralTools.UI
{
    public partial class AdjustRailEndWindow : Window
    {
        public double ValueMm { get; private set; }

        public AdjustRailEndWindow()
        {
            InitializeComponent();
            Loaded += (sender, args) =>
            {
                ValueTextBox.Focus();
                ValueTextBox.SelectAll();
            };
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            string text = (ValueTextBox.Text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                MessageBox.Show(this, "Informe um valor numérico válido em milímetros.",
                    "SAGA - Ajustar extremidade", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ValueMm = value;
            DialogResult = true;
        }

        private void ValueTextBox_GotKeyboardFocus(object sender, RoutedEventArgs e)
        {
            (sender as TextBox)?.SelectAll();
        }
    }
}
