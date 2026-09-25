using SAGAStructuralTools.Core.Domain;
using System.Collections.Generic;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    /// <summary>
    /// Pede o(s) nó(s) do Robot que servirão de referência de inserção. Sem ViewModel — validação
    /// simples em code-behind, mesmo padrão das outras janelas pequenas do projeto.
    /// </summary>
    public partial class RobotReferencePointWindow : Window
    {
        private readonly Dictionary<int, RobotNode> _nodesById;

        public int AnchorNodeId { get; private set; }
        public int? DirectionNodeId { get; private set; }

        public RobotReferencePointWindow(IEnumerable<RobotMember> members)
        {
            InitializeComponent();
            _nodesById = new Dictionary<int, RobotNode>();
            foreach (var m in members)
            {
                _nodesById[m.Start.Id] = m.Start;
                _nodesById[m.End.Id] = m.End;
            }
        }

        private void OnNodeIdChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ErrorText.Text = "";

            bool anchorOk = TryEcho(AnchorNodeBox.Text, AnchorEcho, out int anchorId);
            bool directionProvided = !string.IsNullOrWhiteSpace(DirectionNodeBox.Text);
            bool directionOk = !directionProvided || TryEcho(DirectionNodeBox.Text, DirectionEcho, out int _);

            if (directionProvided && directionOk && anchorOk &&
                int.TryParse(DirectionNodeBox.Text.Trim(), out int dirId) && dirId == anchorId)
            {
                DirectionEcho.Text = "";
                ErrorText.Text = "O nó de direção precisa ser diferente do nó de referência.";
                directionOk = false;
            }

            ContinueButton.IsEnabled = anchorOk && directionOk;
        }

        private bool TryEcho(string text, System.Windows.Controls.TextBlock echo, out int id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(text)) { echo.Text = ""; return false; }

            if (!int.TryParse(text.Trim(), out id))
            {
                echo.Text = "ID inválido";
                echo.Foreground = System.Windows.Media.Brushes.Firebrick;
                return false;
            }

            if (!_nodesById.TryGetValue(id, out var node))
            {
                echo.Text = "nó não encontrado no arquivo";
                echo.Foreground = System.Windows.Media.Brushes.Firebrick;
                return false;
            }

            echo.Text = $"({node.X:F4}, {node.Y:F4}, {node.Z:F4})";
            echo.Foreground = System.Windows.Media.Brushes.Gray;
            return true;
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            AnchorNodeId = int.Parse(AnchorNodeBox.Text.Trim());
            DirectionNodeId = string.IsNullOrWhiteSpace(DirectionNodeBox.Text)
                ? (int?)null
                : int.Parse(DirectionNodeBox.Text.Trim());
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
