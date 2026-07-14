using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SAGAStructuralTools.UI
{
    public partial class VotingMachineWindow : Window
    {
        private string _digits = "";
        private bool _confirming;

        public VotingMachineWindow()
        {
            InitializeComponent();
            Loaded += (_, __) => Focus();
        }

        private void OnNumberClick(object sender, RoutedEventArgs e)
        {
            if (_confirming || _digits.Length >= 2) return;
            if (sender is Button button && button.Tag is string digit)
                AddDigit(digit);
        }

        private void AddDigit(string digit)
        {
            if (_digits.Length >= 2) return;
            _digits += digit;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            NumberDisplay.Text = _digits.PadRight(2, '_');
            bool isThirteen = _digits == "13";
            CandidatePanel.Visibility = isThirteen ? Visibility.Visible : Visibility.Collapsed;

            FeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(52, 52, 52));
            FeedbackText.Text = _digits.Length < 2
                ? "Digite dois números e pressione CONFIRMA"
                : isThirteen
                    ? "Confira os dados e pressione CONFIRMA"
                    : "CANDIDATO NÃO ENCONTRADO";
        }

        private void OnCorrectClick(object sender, RoutedEventArgs e) => ClearVote();

        private void OnWhiteClick(object sender, RoutedEventArgs e)
        {
            ClearVote();
            ShowTryAgain("VOTO EM BRANCO NÃO LIBERA A CRIAÇÃO — TENTE NOVAMENTE");
        }

        private async void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            if (_confirming) return;

            if (_digits != "13")
            {
                ClearVote();
                ShowTryAgain("NÚMERO INCORRETO — TENTE NOVAMENTE");
                return;
            }

            _confirming = true;
            FeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(24, 120, 48));
            FeedbackText.Text = "VOTO CONFIRMADO — FIM";
            await Task.Delay(750);
            DialogResult = true;
        }

        private void ClearVote()
        {
            _digits = "";
            NumberDisplay.Text = "__";
            CandidatePanel.Visibility = Visibility.Collapsed;
            FeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(52, 52, 52));
            FeedbackText.Text = "Digite dois números e pressione CONFIRMA";
        }

        private void ShowTryAgain(string message)
        {
            FeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
            FeedbackText.Text = message;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                AddDigit(((int)e.Key - (int)Key.D0).ToString());
                e.Handled = true;
            }
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                AddDigit(((int)e.Key - (int)Key.NumPad0).ToString());
                e.Handled = true;
            }
            else if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                ClearVote();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                OnConfirmClick(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        }
    }
}
