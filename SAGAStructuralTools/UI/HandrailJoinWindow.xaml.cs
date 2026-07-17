using SAGAStructuralTools.UI.Converters;
using System.Globalization;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class HandrailJoinWindow : Window
    {
        public double RadiusMm { get; private set; }
        public double ReferenceHeightMm { get; private set; }
        public double HorizontalOffsetMm { get; private set; }

        public HandrailJoinWindow(
            string firstProfile,
            string secondProfile,
            string thirdProfile,
            string transitionDescription,
            double initialRadiusMm,
            bool showsSagaEditWarning,
            double? initialReferenceHeightMm = null,
            double? initialHorizontalOffsetMm = null,
            int batchIndex = 0,
            int batchCount = 0)
        {
            InitializeComponent();

            if (batchCount > 1 && batchIndex >= 0 && batchIndex < batchCount)
            {
                int displayIndex = batchIndex + 1;
                DialogHeading.Text = $"SAGA - Unir corrimãos — Par {displayIndex} de {batchCount}";
                ApplyButton.Content = displayIndex < batchCount
                    ? $"Confirmar par {displayIndex} e próximo"
                    : "Confirmar e criar lote";
                ApplyButton.Width = 180;
            }

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
            if (initialReferenceHeightMm.HasValue)
            {
                ReferenceHeightPanel.Visibility = Visibility.Visible;
                ReferenceHeightTextBox.Text = initialReferenceHeightMm.Value.ToString(
                    "0.########",
                    CultureInfo.GetCultureInfo("pt-BR"));
            }
            if (initialHorizontalOffsetMm.HasValue)
            {
                HorizontalOffsetPanel.Visibility = Visibility.Visible;
                HorizontalOffsetTextBox.Text = initialHorizontalOffsetMm.Value.ToString(
                    "0.########",
                    CultureInfo.GetCultureInfo("pt-BR"));
            }
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

            if (ReferenceHeightPanel.Visibility == Visibility.Visible &&
                (!FlexibleDoubleConverter.TryParse(
                     ReferenceHeightTextBox.Text,
                     out double referenceHeightMm) ||
                 referenceHeightMm < -100000 || referenceHeightMm > 100000))
            {
                ValidationText.Text =
                    "Informe uma altura entre -100000 e 100000 mm em relação à aresta.";
                ReferenceHeightTextBox.Focus();
                ReferenceHeightTextBox.SelectAll();
                return;
            }

            if (HorizontalOffsetPanel.Visibility == Visibility.Visible &&
                (!FlexibleDoubleConverter.TryParse(
                     HorizontalOffsetTextBox.Text,
                     out double horizontalOffsetMm) ||
                 horizontalOffsetMm < 0 || horizontalOffsetMm > 100000))
            {
                ValidationText.Text =
                    "Informe um deslocamento entre 0 e 100000 mm; o ponto define o lado.";
                HorizontalOffsetTextBox.Focus();
                HorizontalOffsetTextBox.SelectAll();
                return;
            }

            RadiusMm = radiusMm;
            if (ReferenceHeightPanel.Visibility == Visibility.Visible)
            {
                FlexibleDoubleConverter.TryParse(
                    ReferenceHeightTextBox.Text,
                    out double parsedReferenceHeightMm);
                ReferenceHeightMm = parsedReferenceHeightMm;
            }
            if (HorizontalOffsetPanel.Visibility == Visibility.Visible)
            {
                FlexibleDoubleConverter.TryParse(
                    HorizontalOffsetTextBox.Text,
                    out double parsedHorizontalOffsetMm);
                HorizontalOffsetMm = parsedHorizontalOffsetMm;
            }
            DialogResult = true;
        }
    }
}
