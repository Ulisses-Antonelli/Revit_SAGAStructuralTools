using SAGAStructuralTools.UI.Converters;
using System.Globalization;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class HandrailJoinWindow : Window
    {
        public double RadiusMm { get; private set; }

        public HandrailJoinWindow(
            string firstProfile,
            string secondProfile,
            string thirdProfile,
            string transitionDescription,
            double initialRadiusMm,
            bool showsSagaEditWarning)
        {
            InitializeComponent();

            FirstProfileText.Text = firstProfile ?? "Trecho 1";
            SecondProfileText.Text = secondProfile ?? "Trecho 2";
            TransitionText.Text = string.IsNullOrWhiteSpace(transitionDescription)
                ? "Transição entre corrimãos"
                : transitionDescription;

            if (string.IsNullOrWhiteSpace(thirdProfile))
            {
                ThirdProfileText.Text = null;
                ThirdProfileText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ThirdProfileText.Text = thirdProfile;
                ThirdProfileText.Visibility = Visibility.Visible;
            }

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
                ValidationText.Text =
                    "Informe um raio maior que zero. Exemplo: 100 ou 18,875.";
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
