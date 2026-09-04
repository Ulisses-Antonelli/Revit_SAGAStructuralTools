using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.NavisworksBridge;
using System;
using System.Collections.Generic;
using WinForms = System.Windows.Forms;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportNavisworksBridgeCommand : IExternalCommand
    {
        private const ObjectSnapTypes SnapTypes =
            ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections |
            ObjectSnapTypes.Centers | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Nearest;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;

            string jsonPath;
            using (var dlg = new WinForms.OpenFileDialog
            {
                Title = "Selecionar arquivo-ponte do Navisworks",
                Filter = "Arquivo-ponte SAGA (*.json)|*.json"
            })
            {
                if (dlg.ShowDialog() != WinForms.DialogResult.OK)
                    return Result.Cancelled;
                jsonPath = dlg.FileName;
            }

            try
            {
                BridgeFile file = BridgeGeometryImporter.ReadFile(jsonPath);
                if (file.itens == null || file.itens.Count == 0)
                {
                    WinForms.MessageBox.Show("O arquivo-ponte não contém itens.", "SAGA — Importar Malha (Navisworks)");
                    return Result.Cancelled;
                }

                var warnings = new List<string>();
                if (!TryGetPlacement(uidoc, file, warnings, out Transform placement))
                    return Result.Cancelled;

                BridgeGeometryImporter.ImportResult result;
                using (var tx = new Transaction(doc, "Importar malha do Navisworks"))
                {
                    tx.Start();
                    result = BridgeGeometryImporter.BuildAndInsert(doc, file, placement);
                    tx.Commit();
                }

                result.Warnings.InsertRange(0, warnings);

                string warningText = result.Warnings.Count > 0
                    ? "\n\nAvisos:\n" + string.Join("\n", result.Warnings)
                    : "";

                string idsText = result.CreatedShapes.Count > 0
                    ? "\n\nElementId(s) criado(s) (use Gerenciar > Consulta > Selecionar por ID):\n" +
                      string.Join("\n", result.CreatedShapes.ConvertAll(s => $"{s.ElementId} — {s.Name}"))
                    : "";

                WinForms.MessageBox.Show(
                    $"{result.ShapeCount} elemento(s) criado(s) a partir de {result.ItemCount} item(ns), " +
                    $"{result.TriangleCount} triângulo(s) ({result.SkippedTriangleCount} descartado(s))." +
                    warningText + idsText,
                    "SAGA — Importar Malha (Navisworks)");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("ImportNavisworksBridgeCommand.Execute", ex);
                message = $"Erro ao importar: {ex.Message}";
                return Result.Failed;
            }
        }

        /// <summary>
        /// Pergunta onde o 0,0,0 do arquivo-ponte (o ponto de referência clicado no
        /// Navisworks) deve cair no Revit e monta o transform de inserção.
        /// </summary>
        private static bool TryGetPlacement(UIDocument uidoc, BridgeFile file, List<string> warnings, out Transform placement)
        {
            placement = Transform.Identity;

            var td = new TaskDialog("SAGA — Referência de Inserção")
            {
                MainInstruction = "Onde fica o ponto 0,0,0 do arquivo-ponte?",
                MainContent = "É o ponto de referência que foi clicado na exportação do Navisworks.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Usar a Origem Interna (0,0,0)",
                "Para quando ainda não há geometria de referência modelada no Revit.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Escolher um ponto no modelo",
                "Para quando já existe geometria de referência — clique o ponto correspondente com snap.");

            TaskDialogResult choice = td.Show();
            if (choice == TaskDialogResult.CommandLink1)
                return true;
            if (choice != TaskDialogResult.CommandLink2)
                return false;

            XYZ originPoint = uidoc.Selection.PickPoint(SnapTypes,
                "SAGA — clique o ponto correspondente à referência definida no Navisworks");

            double angleRad = 0;
            double[] direcao = file.referencia?.direcao;
            if (direcao != null && direcao.Length >= 2)
            {
                XYZ directionPoint = uidoc.Selection.PickPoint(SnapTypes,
                    "SAGA — clique o ponto correspondente à direção definida no Navisworks");
                angleRad = ComputeZAngle(direcao, directionPoint - originPoint, warnings);
            }

            placement = Transform.CreateRotation(XYZ.BasisZ, angleRad);
            placement.Origin = originPoint;
            return true;
        }

        /// <summary>
        /// Ângulo em torno de Z entre a direção de referência do Navisworks e a
        /// direção clicada no Revit, ambas projetadas no plano XY.
        /// </summary>
        private static double ComputeZAngle(double[] direcaoNavis, XYZ direcaoRevit, List<string> warnings)
        {
            double navisPlanar = Math.Sqrt(direcaoNavis[0] * direcaoNavis[0] + direcaoNavis[1] * direcaoNavis[1]);
            double revitPlanar = Math.Sqrt(direcaoRevit.X * direcaoRevit.X + direcaoRevit.Y * direcaoRevit.Y);

            if (navisPlanar < 1e-6 || revitPlanar < 1e-9)
            {
                warnings.Add("Direção quase vertical em um dos lados — rotação ignorada (assumida zero).");
                return 0;
            }

            double angle = Math.Atan2(direcaoRevit.Y, direcaoRevit.X) - Math.Atan2(direcaoNavis[1], direcaoNavis[0]);
            SagaLog.Write($"Rotação de inserção: {angle * 180.0 / Math.PI:F3}°.");
            return angle;
        }
    }
}
