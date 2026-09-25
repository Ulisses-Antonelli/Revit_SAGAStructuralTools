using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Robot;
using SAGAStructuralTools.UI.ViewModels;
using System.Windows;

namespace SAGAStructuralTools.UI
{
    public partial class ImportRobotModelWindow : Window
    {
        public ImportRobotModelWindow(Document doc, RobotParseResult parseResult, RobotPlacementAnchor anchor)
        {
            InitializeComponent();
            DataContext = new ImportRobotModelViewModel(doc, parseResult, anchor);
        }
    }
}
