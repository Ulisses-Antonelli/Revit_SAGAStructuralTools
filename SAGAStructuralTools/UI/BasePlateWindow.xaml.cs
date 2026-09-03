using SAGAStructuralTools.BasePlate.Domain;
using SAGAStructuralTools.UI.Converters;
using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SAGAStructuralTools.UI
{
    public partial class BasePlateWindow : Window
    {
        public BasePlateWindow()
        {
            InitializeComponent();
            SummaryTextBlock.Text = "Preencha os dados e clique em Calcular.";
        }

        private void Calculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BasePlateCalculationResult result =
                    new BasePlateCalculator().Calculate(ReadInput());

                SummaryTextBlock.Foreground = result.IsApproved
                    ? new SolidColorBrush(Color.FromRgb(0x0B, 0x6B, 0x3A))
                    : new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
                SummaryTextBlock.Text = result.IsApproved
                    ? "Resultado preliminar: aprovado nas verificações iniciais."
                    : "Resultado preliminar: reprovado nas verificações iniciais.";
                VerificationTextBlock.Text = FormatVerifications(result);
            }
            catch (Exception ex)
            {
                SummaryTextBlock.Foreground =
                    new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
                SummaryTextBlock.Text = "Não foi possível calcular.";
                VerificationTextBlock.Text = ex.Message;
            }
        }

        private BasePlateInput ReadInput()
        {
            return new BasePlateInput
            {
                DepthMm = ReadDouble(DepthTextBox, "Altura d"),
                FlangeWidthMm = ReadDouble(FlangeWidthTextBox, "Mesa bf"),
                WebThicknessMm = ReadDouble(WebThicknessTextBox, "Alma tw"),
                FlangeThicknessMm = ReadDouble(FlangeThicknessTextBox, "Mesa tf"),
                AxialForceTf = ReadDouble(AxialForceTextBox, "Normal N"),
                MomentX_TfM = ReadDouble(MomentXTextBox, "Momento Mx"),
                MomentY_TfM = ReadDouble(MomentYTextBox, "Momento My"),
                ShearX_Tf = ReadDouble(ShearXTextBox, "Cortante Vx"),
                ShearY_Tf = ReadDouble(ShearYTextBox, "Cortante Vy"),
                PlateFyMpa = ReadDouble(PlateFyTextBox, "Placa fy"),
                PlateFuMpa = ReadDouble(PlateFuTextBox, "Placa fu"),
                ConcreteFckMpa = ReadDouble(ConcreteFckTextBox, "Concreto fck"),
                AnchorFyMpa = ReadDouble(AnchorFyTextBox, "Chumbador fy"),
                AnchorFuMpa = ReadDouble(AnchorFuTextBox, "Chumbador fu"),
                ColumnFyMpa = ReadDouble(ColumnFyTextBox, "Coluna fy"),
                ColumnFuMpa = ReadDouble(ColumnFuTextBox, "Coluna fu"),
                PlateLengthXmm = ReadDouble(PlateLengthXTextBox, "Lx"),
                PlateLengthYmm = ReadDouble(PlateLengthYTextBox, "Ly"),
                PlateThicknessMm = ReadDouble(PlateThicknessTextBox, "Espessura"),
                AnchorDiameterMm = ReadDouble(AnchorDiameterTextBox, "Diâmetro"),
                EmbedmentLengthMm = ReadDouble(EmbedmentLengthTextBox, "Embutimento"),
                HasHook = HasHookCheckBox.IsChecked == true,
                CorrosionAllowanceMm = ReadDouble(CorrosionAllowanceTextBox, "Corrosão"),
                AnchorsX = ReadInt(AnchorsXTextBox, "Quantidade X"),
                AnchorsY = ReadInt(AnchorsYTextBox, "Quantidade Y"),
                AnchorEdgeDistanceXmm = ReadDouble(AnchorEdgeDistanceXTextBox, "Borda X"),
                AnchorEdgeDistanceYmm = ReadDouble(AnchorEdgeDistanceYTextBox, "Borda Y"),
                StiffenerThicknessMm = ReadDouble(StiffenerThicknessTextBox, "Espessura da nervura"),
                StiffenerHeightMm = ReadDouble(StiffenerHeightTextBox, "Altura da nervura"),
                HasMiddleStiffener = HasMiddleStiffenerCheckBox.IsChecked == true
            };
        }

        private static double ReadDouble(TextBox textBox, string fieldName)
        {
            if (FlexibleDoubleConverter.TryParse(textBox.Text, out double value))
                return value;

            textBox.Focus();
            textBox.SelectAll();
            throw new InvalidOperationException(
                $"Informe um valor numérico válido para {fieldName}.");
        }

        private static int ReadInt(TextBox textBox, string fieldName)
        {
            if (int.TryParse(
                    textBox.Text,
                    NumberStyles.Integer,
                    CultureInfo.GetCultureInfo("pt-BR"),
                    out int value))
                return value;

            textBox.Focus();
            textBox.SelectAll();
            throw new InvalidOperationException(
                $"Informe um número inteiro válido para {fieldName}.");
        }

        private static string FormatVerifications(BasePlateCalculationResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                $"Diâmetro corroído do chumbador: {Format(result.CorrodedAnchorDiameterMm)} mm");
            builder.AppendLine();

            foreach (VerificationResult verification in result.Verifications)
            {
                builder.Append(FormatStatus(verification.Status));
                builder.Append(" ");
                builder.Append(verification.Name);
                builder.Append(": ");
                builder.Append(Format(verification.CalculatedValue));
                builder.Append(" / mínimo ");
                builder.Append(Format(verification.RequiredValue));
                if (!string.IsNullOrWhiteSpace(verification.Unit))
                {
                    builder.Append(" ");
                    builder.Append(verification.Unit);
                }
                builder.Append(" - ");
                builder.AppendLine(verification.Message);
            }

            return builder.ToString();
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"));
        }

        private static string FormatStatus(VerificationStatus status)
        {
            if (status == VerificationStatus.Passed) return "OK";
            if (status == VerificationStatus.Warning) return "AVISO";
            return "FALHA";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
