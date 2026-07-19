using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class SaveRailPresetWindow : Window
    {
        public string PresetName { get; private set; }

        public SaveRailPresetWindow()
        {
            InitializeComponent();
            Loaded += (sender, args) => PresetNameTextBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string name = PresetNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ValidationText.Text = "Informe um nome para o padrão.";
                PresetNameTextBox.Focus();
                return;
            }

            PresetName = name;
            DialogResult = true;
        }
    }
}
