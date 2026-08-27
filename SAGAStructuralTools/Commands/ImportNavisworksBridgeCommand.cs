using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAGAStructuralTools.Core.NavisworksBridge;
using System;
using System.Windows.Forms;

namespace SAGAStructuralTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportNavisworksBridgeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            using (var dlg = new OpenFileDialog
            {
                Title = "Selecionar arquivo-ponte do Navisworks",
                Filter = "Arquivo-ponte SAGA (*.json)|*.json"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;

                try
                {
                    BridgeGeometryImporter.ImportResult result;
                    using (var tx = new Transaction(doc, "Importar malha do Navisworks"))
                    {
                        tx.Start();
                        result = BridgeGeometryImporter.Import(doc, dlg.FileName);
                        tx.Commit();
                    }

                    string warningText = result.Warnings.Count > 0
                        ? "\n\nAvisos:\n" + string.Join("\n", result.Warnings)
                        : "";

                    string idsText = result.CreatedShapes.Count > 0
                        ? "\n\nElementId(s) criado(s) (use Gerenciar > Consulta > Selecionar por ID):\n" +
                          string.Join("\n", result.CreatedShapes.ConvertAll(s => $"{s.ElementId} — {s.Name}"))
                        : "";

                    MessageBox.Show(
                        $"{result.ShapeCount} elemento(s) criado(s) a partir de {result.ItemCount} item(ns), " +
                        $"{result.TriangleCount} triângulo(s) ({result.SkippedTriangleCount} descartado(s))." +
                        warningText + idsText,
                        "SAGA — Importar Malha (Navisworks)");

                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    SagaLog.Exception("ImportNavisworksBridgeCommand.Execute", ex);
                    message = $"Erro ao importar: {ex.Message}";
                    return Result.Failed;
                }
            }
        }
    }
}
