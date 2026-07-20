using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SAGAStructuralTools.Core.Rail;
using System;
using System.Linq;

namespace SAGAStructuralTools.Core.Ladder
{
    /// <summary>
    /// UC-01 — seleção da viga superior em um único clique: o usuário clica na viga
    /// PELO LADO onde a escada será instalada. O ponto do clique, projetado no eixo,
    /// define o ponto de inserção; o vetor horizontal do eixo até o clique define o
    /// lado (lateral). A elevação superior vem da face de topo da viga e o nível-base
    /// é o nível mais próximo abaixo do desembarque.
    /// </summary>
    public class LadderPickHandler : IExternalEventHandler
    {
        private const double MinSideOffsetFt = 10.0 / 304.8;   // clique a ≥10 mm do eixo
        private const double MinLevelDropMm  = 500.0;          // nível-base ≥500 mm abaixo

        public event Action<LadderPlacement> PlacementPicked;
        public event Action<string>          PickFailed;

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc   = uidoc.Document;

                var reference = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new StructuralFramingFilter(),
                    "Clique na viga superior PELO LADO onde a escada será instalada (ESC para cancelar).");

                var beam       = doc.GetElement(reference.ElementId);
                var clickPoint = reference.GlobalPoint;

                if (!(beam?.Location is LocationCurve lc) || !(lc.Curve is Line axis))
                {
                    PickFailed?.Invoke("A viga selecionada não possui um eixo reto válido.");
                    return;
                }

                // Projeção do clique no eixo → ponto de inserção da escada.
                var projection = axis.Project(clickPoint);
                var insertion  = projection?.XYZPoint ?? clickPoint;

                // Lado: componente horizontal do vetor eixo→clique.
                var side = new XYZ(clickPoint.X - insertion.X, clickPoint.Y - insertion.Y, 0);
                if (side.GetLength() < MinSideOffsetFt)
                {
                    PickFailed?.Invoke(
                        "Não foi possível identificar o lado da escada. Clique na FACE LATERAL da viga, " +
                        "do lado onde a escada será instalada (evite o topo/centro).");
                    return;
                }
                var lateral = side.Normalize();

                // Desembarque = face superior da viga (bounding box).
                var bb = beam.get_BoundingBox(null);
                if (bb == null)
                {
                    PickFailed?.Invoke("Não foi possível ler a geometria da viga selecionada.");
                    return;
                }
                double topZ = bb.Max.Z;

                // Meia-largura real da viga na direção da escada (geometria tesselada,
                // vale para qualquer seção — I, tubo, chato).
                double beamWidthMm = GeometryMeasure.ExtentAlongMm(doc, beam.Id, lateral);
                double halfWidthMm = beamWidthMm > 1.0 ? beamWidthMm / 2.0 : 0.0;

                // Nível-base: o nível mais alto que esteja suficientemente abaixo do topo.
                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .Where(l => (topZ - l.Elevation) * 304.8 >= MinLevelDropMm)
                    .OrderByDescending(l => l.Elevation)
                    .FirstOrDefault();

                if (level == null)
                {
                    PickFailed?.Invoke(
                        "Nenhum nível encontrado abaixo do desembarque. Crie o nível do piso inferior " +
                        "ou verifique a viga selecionada.");
                    return;
                }

                PlacementPicked?.Invoke(new LadderPlacement
                {
                    InsertionPoint  = insertion,
                    Lateral         = lateral,
                    TopZFt          = topZ,
                    BaseZFt         = level.Elevation,
                    BeamHalfWidthMm = halfWidthMm,
                    BeamName        = beam.Name,
                    LevelName       = level.Name,
                    BeamId          = beam.Id
                });
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // ESC — fluxo normal, nenhuma ação.
            }
            catch (Exception ex)
            {
                PickFailed?.Invoke($"Falha na seleção da referência: {ex.Message}");
            }
        }

        public string GetName() => "SAGAPickLadderReference";

        private sealed class StructuralFramingFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                element?.Category?.Id.GetId() == (int)BuiltInCategory.OST_StructuralFraming;

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
