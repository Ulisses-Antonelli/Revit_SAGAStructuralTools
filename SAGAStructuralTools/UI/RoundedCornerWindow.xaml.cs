using SAGAStructuralTools.UI.Converters;
using System.Globalization;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class RoundedCornerWindow : Window
    {
        public double RadiusMm { get; private set; }

        public RoundedCornerWindow(
            string firstProfile,
            string secondProfile,
            double initialRadiusMm,
            bool showsSagaEditWarning)
        {
            InitializeComponent();

            FirstProfileText.Text = firstProfile ?? "Perfil 1";
            SecondProfileText.Text = secondProfile ?? "Perfil 2";
            RadiusTextBox.Text = initialRadiusMm.ToString(
                "0.########",
                CultureInfo.GetCultureInfo("pt-BR"));
            SagaEditWarning.Visibility = showsSagaEditWarning
                ? Visibility.Visible
                : Visibility.Collapsed;

            Loaded += (sender, args) =>
            {
                RadiusTextBox.Focus();
                RadiusTextBox.SelectAll();
            };
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ValidationText.Text = null;
            if (!FlexibleDoubleConverter.TryParse(RadiusTextBox.Text, out double radiusMm) ||
                radiusMm <= 0)
            {
                ValidationText.Text = "Informe um raio maior que zero. Exemplo: 100 ou 18,875.";
                RadiusTextBox.Focus();
                RadiusTextBox.SelectAll();
                return;
            }

            if (radiusMm > 100000)
            {
                ValidationText.Text = "O raio informado é maior que 100000 mm.";
                RadiusTextBox.Focus();
                RadiusTextBox.SelectAll();
                return;
            }

            RadiusMm = radiusMm;
            DialogResult = true;
        }
    }
}
