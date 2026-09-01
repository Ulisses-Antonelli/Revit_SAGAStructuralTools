using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class FlatshotWindow : Window
    {
        public string ViewName { get; private set; }
        public bool OpenAfterCreate { get; private set; }

        public FlatshotWindow(string defaultViewName)
        {
            InitializeComponent();
            ViewNameTextBox.Text = defaultViewName;
            Loaded += (sender, args) =>
            {
                ViewNameTextBox.Focus();
                ViewNameTextBox.SelectAll();
            };
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            string name = ViewNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ValidationText.Text = "Informe um nome para a vista de registro.";
                ViewNameTextBox.Focus();
                return;
            }

            ViewName = name;
            OpenAfterCreate = OpenAfterCreateCheckBox.IsChecked == true;
            DialogResult = true;
        }
    }
}
