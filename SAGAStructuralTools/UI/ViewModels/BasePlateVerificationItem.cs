using System.Windows.Media;

namespace SAGAStructuralTools.UI.ViewModels
{
    public class BasePlateVerificationItem
    {
        public string Name { get; set; }
        public bool IsOk { get; set; }
        public string ResultText { get; set; }
        public double? Utilization { get; set; }
        public string GoverningCase { get; set; }
        public string Icon { get; set; }
        public Brush Background { get; set; }
        public Brush Foreground { get; set; }

        public static BasePlateVerificationItem Create(
            string name,
            bool isOk,
            string errorMessage,
            double? utilization = null,
            string governingCase = null)
        {
            return new BasePlateVerificationItem
            {
                Name = name,
                IsOk = isOk,
                ResultText = isOk ? "OK!" : errorMessage,
                Utilization = utilization,
                GoverningCase = governingCase,
                Icon = isOk ? "✓" : "✕",
                Background = isOk
                    ? new SolidColorBrush(Color.FromRgb(0xD8, 0xF3, 0xDC))
                    : new SolidColorBrush(Color.FromRgb(0xFD, 0xE2, 0xE2)),
                Foreground = isOk
                    ? new SolidColorBrush(Color.FromRgb(0x0B, 0x6B, 0x3A))
                    : new SolidColorBrush(Color.FromRgb(0x8A, 0x12, 0x12))
            };
        }
    }
}
