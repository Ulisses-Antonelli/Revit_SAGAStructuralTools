using SAGAStructuralTools.UI.Converters;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public sealed class BatchHandrailJoinRow
    {
        public int Number { get; set; }
        public string Description { get; set; }
        public string HeightText { get; set; }
        public bool IsHeightEditable { get; set; }
        public string Error { get; set; }
    }

    public partial class BatchHandrailJoinWindow : Window
    {
        private readonly Func<int, double, double, double, string> _validatePair;
        private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pt-BR");

        public ObservableCollection<BatchHandrailJoinRow> Rows { get; }
        public double RadiusMm { get; private set; }
        public double OffsetMm { get; private set; }
        public double[] HeightsMm { get; private set; }

        public BatchHandrailJoinWindow(
            string[] descriptions,
            double[] initialHeightsMm,
            double initialRadiusMm,
            double initialOffsetMm,
            bool[] heightEditable,
            Func<int, double, double, double, string> validatePair)
        {
            InitializeComponent();
            _validatePair = validatePair;
            Rows = new ObservableCollection<BatchHandrailJoinRow>();
            for (int index = 0; index < descriptions.Length; index++)
            {
                Rows.Add(new BatchHandrailJoinRow
                {
                    Number = index + 1,
                    Description = descriptions[index],
                    HeightText = initialHeightsMm[index].ToString("0.########", _culture),
                    IsHeightEditable =
                        heightEditable == null ||
                        index >= heightEditable.Length ||
                        heightEditable[index]
                });
            }

            DataContext = this;
            RadiusTextBox.Text = initialRadiusMm.ToString("0.########", _culture);
            OffsetTextBox.Text = initialOffsetMm.ToString("0.########", _culture);
            var firstEditableHeight =
                Rows.FirstOrDefault(row => row.IsHeightEditable);
            CommonHeightTextBox.Text =
                firstEditableHeight?.HeightText ?? "0";
            SameHeightCheckBox.IsEnabled = firstEditableHeight != null;
        }

        private void SameHeight_Checked(object sender, RoutedEventArgs e)
        {
            CommonHeightTextBox.IsEnabled = true;
            ApplyHeightButton.IsEnabled = true;
            CommonHeightTextBox.Focus();
            CommonHeightTextBox.SelectAll();
        }

        private void SameHeight_Unchecked(object sender, RoutedEventArgs e)
        {
            CommonHeightTextBox.IsEnabled = false;
            ApplyHeightButton.IsEnabled = false;
        }

        private void ApplyHeight_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseBounded(CommonHeightTextBox.Text, out double heightMm, true))
            {
                ValidationText.Text = "Informe uma altura comum entre -100000 e 100000 mm.";
                return;
            }

            string text = heightMm.ToString("0.########", _culture);
            foreach (var row in Rows)
            {
                if (row.IsHeightEditable)
                    row.HeightText = text;
            }
            PairsGrid.Items.Refresh();
            ValidationText.Text = null;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ValidationText.Text = null;
            if (!TryParseBounded(RadiusTextBox.Text, out double radiusMm, false) || radiusMm <= 0)
            {
                ValidationText.Text = "Informe um raio comum maior que zero e menor que 100000 mm.";
                return;
            }
            if (!TryParseBounded(OffsetTextBox.Text, out double offsetMm, false))
            {
                ValidationText.Text = "Informe um deslocamento entre 0 e 100000 mm.";
                return;
            }

            var heights = new double[Rows.Count];
            bool hasError = false;
            for (int index = 0; index < Rows.Count; index++)
            {
                var row = Rows[index];
                row.Error = null;
                if (!TryParseBounded(row.HeightText, out heights[index], true))
                {
                    row.Error = "Altura inválida";
                    hasError = true;
                    continue;
                }

                string error = _validatePair?.Invoke(index, heights[index], offsetMm, radiusMm);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    row.Error = error;
                    hasError = true;
                }
                else
                {
                    row.Error = "OK";
                }
            }
            PairsGrid.Items.Refresh();

            if (hasError)
            {
                ValidationText.Text =
                    "Um ou mais pares não puderam ser validados. Ajuste as linhas indicadas e tente novamente.";
                return;
            }

            RadiusMm = radiusMm;
            OffsetMm = offsetMm;
            HeightsMm = heights;
            DialogResult = true;
        }

        private static bool TryParseBounded(string text, out double value, bool allowNegative)
        {
            if (!FlexibleDoubleConverter.TryParse(text, out value))
                return false;
            double minimum = allowNegative ? -100000 : 0;
            return value >= minimum && value <= 100000;
        }
    }
}
