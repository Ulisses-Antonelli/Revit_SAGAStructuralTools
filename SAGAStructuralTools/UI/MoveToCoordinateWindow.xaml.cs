using Autodesk.Revit.DB;
using SAGAStructuralTools.UI.Converters;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace SAGAStructuralTools.UI
{
    public partial class MoveToCoordinateWindow : Window
    {
        private const double MillimetersPerFoot = 304.8;
        private readonly XYZ _internalPoint;
        private readonly Transform _internalToShared;

        public XYZ TargetCoordinate { get; private set; }
        public bool UsesSharedCoordinates { get; private set; }

        public MoveToCoordinateWindow(
            string elementDescription,
            XYZ internalPoint,
            Transform internalToShared)
        {
            InitializeComponent();
            ElementText.Text = elementDescription;
            _internalPoint = internalPoint;
            _internalToShared = internalToShared ?? Transform.Identity;
            CoordinateSystemComboBox.SelectedIndex = 0;
            ShowCoordinates(_internalPoint);

            Loaded += (sender, args) =>
            {
                XTextBox.Focus();
                XTextBox.SelectAll();
            };
        }

        private void CoordinateSystem_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!IsLoaded && CoordinateSystemComboBox.SelectedItem == null) return;
            bool shared = IsSharedSelected();
            ShowCoordinates(shared
                ? _internalToShared.OfPoint(_internalPoint)
                : _internalPoint);
        }

        private void ShowCoordinates(XYZ point)
        {
            if (point == null || XTextBox == null) return;
            var culture = CultureInfo.GetCultureInfo("pt-BR");
            string x = (point.X * MillimetersPerFoot).ToString("0.###", culture);
            string y = (point.Y * MillimetersPerFoot).ToString("0.###", culture);
            string z = (point.Z * MillimetersPerFoot).ToString("0.###", culture);
            CurrentXText.Text = x;
            CurrentYText.Text = y;
            CurrentZText.Text = z;
            XTextBox.Text = x;
            YTextBox.Text = y;
            ZTextBox.Text = z;
        }

        private void Move_Click(object sender, RoutedEventArgs e)
        {
            ValidationText.Text = null;
            if (UseXCheckBox.IsChecked != true &&
                UseYCheckBox.IsChecked != true &&
                UseZCheckBox.IsChecked != true)
            {
                ValidationText.Text = "Marque pelo menos um eixo para realizar o movimento.";
                return;
            }

            XYZ current = IsSharedSelected()
                ? _internalToShared.OfPoint(_internalPoint)
                : _internalPoint;

            if (!TryReadAxis(XTextBox, UseXCheckBox, current.X, "X", out double x) ||
                !TryReadAxis(YTextBox, UseYCheckBox, current.Y, "Y", out double y) ||
                !TryReadAxis(ZTextBox, UseZCheckBox, current.Z, "Z", out double z))
                return;

            TargetCoordinate = new XYZ(x, y, z);
            UsesSharedCoordinates = IsSharedSelected();
            DialogResult = true;
        }

        private bool TryReadAxis(
            TextBox textBox,
            CheckBox checkBox,
            double currentFeet,
            string axis,
            out double valueFeet)
        {
            valueFeet = currentFeet;
            if (checkBox.IsChecked != true) return true;

            if (!FlexibleDoubleConverter.TryParse(textBox.Text, out double millimeters))
            {
                ValidationText.Text = $"Informe uma coordenada {axis} válida.";
                textBox.Focus();
                textBox.SelectAll();
                return false;
            }

            if (double.IsNaN(millimeters) || double.IsInfinity(millimeters))
            {
                ValidationText.Text = $"A coordenada {axis} não é um número finito válido.";
                textBox.Focus();
                textBox.SelectAll();
                return false;
            }

            valueFeet = millimeters / MillimetersPerFoot;
            return true;
        }

        private bool IsSharedSelected()
        {
            return (CoordinateSystemComboBox.SelectedItem as ComboBoxItem)?.Tag as string
                == "Shared";
        }
    }
}
