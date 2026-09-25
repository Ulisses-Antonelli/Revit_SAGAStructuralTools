using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Robot;
using SAGAStructuralTools.UI;
using System;
using System.Windows.Forms;

namespace SAGAStructuralTools.Commands
{
    /// <summary>
    /// Lê o arquivo texto estruturado exportado pelo Robot (nós + barras + perfis) e recria o modelo
    /// como famílias estruturais nativas do catálogo Gerdau — mesmo princípio do "Converter IFC", mas
    /// sem precisar importar/vincular nada no Revit primeiro, já que o Robot não tem um formato que o
    /// Revit importe nativamente como o IFC.
    ///
    /// Antes de abrir a tela de importação, pede um nó de referência do Robot e o ponto
    /// correspondente no Revit (igual ao bridge do Navisworks) — sem isso o modelo nasceria nas
    /// coordenadas absolutas e arbitrárias do Robot, sem relação com o modelo real.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportRobotModelCommand : IExternalCommand
    {
        private const ObjectSnapTypes SnapTypes =
            ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections |
            ObjectSnapTypes.Centers | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Nearest;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
            {
                message = "Nenhum documento Revit está aberto.";
                return Result.Failed;
            }

            try
            {
                string txtPath;
                using (var fileDlg = new OpenFileDialog
                {
                    Title = "Selecionar o arquivo texto exportado do Robot",
                    Filter = "Arquivo texto do Robot (*.txt)|*.txt|Todos os arquivos (*.*)|*.*"
                })
                {
                    if (fileDlg.ShowDialog() != DialogResult.OK) return Result.Cancelled;
                    txtPath = fileDlg.FileName;
                }

                var parseResult = RobotTextModelParser.Parse(txtPath);
                SagaLog.Write($"ImportRobotModelCommand: '{txtPath}' -> {parseResult.Members.Count} barra(s) com perfil, {parseResult.Warnings.Count} aviso(s) de leitura, unidade='{parseResult.LengthUnit}'.");
                if (parseResult.Members.Count == 0)
                {
                    message = "Nenhuma barra com perfil atribuído foi encontrada no arquivo.";
                    return Result.Failed;
                }

                var refWindow = new RobotReferencePointWindow(parseResult.Members) { Owner = null };
                if (refWindow.ShowDialog() != true) return Result.Cancelled;

                var nodesById = new System.Collections.Generic.Dictionary<int, RobotNode>();
                foreach (var m in parseResult.Members) { nodesById[m.Start.Id] = m.Start; nodesById[m.End.Id] = m.End; }

                var anchorNode = nodesById[refWindow.AnchorNodeId];
                XYZ anchorRevitPoint = uidoc.Selection.PickPoint(SnapTypes,
                    $"SAGA — clique o ponto do Revit correspondente ao nó {refWindow.AnchorNodeId} do Robot");

                RobotNode directionNode = null;
                XYZ directionRevitPoint = null;
                if (refWindow.DirectionNodeId.HasValue)
                {
                    directionNode = nodesById[refWindow.DirectionNodeId.Value];
                    directionRevitPoint = uidoc.Selection.PickPoint(SnapTypes,
                        $"SAGA — clique o ponto do Revit correspondente ao nó {refWindow.DirectionNodeId.Value} do Robot (direção)");
                }

                double toFeet = RobotPlacementAnchor.LengthUnitToFeet(parseResult.LengthUnit);
                var anchor = new RobotPlacementAnchor(anchorNode, anchorRevitPoint, toFeet, directionNode, directionRevitPoint);
                SagaLog.Write($"ImportRobotModelCommand: âncora nó {refWindow.AnchorNodeId} -> {anchorRevitPoint}, rotação={anchor.RotationDegrees:F3}°.");

                var window = new ImportRobotModelWindow(doc, parseResult, anchor);
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("ImportRobotModelCommand.Execute", ex);
                message = $"Erro ao importar o modelo do Robot: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}
