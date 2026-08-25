using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Models;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Stiffener
{
    /// <summary>
    /// Constrói cada chapa de nervura como um <see cref="DirectShape"/> individual
    /// (sólido BIM quantificável, sem dependência de família externa) — mesmo
    /// idioma de <see cref="Stair.TreadBuilder"/>/<see cref="Stair.LandingBuilder"/>.
    /// Nunca funde as duas chapas simétricas num sólido só: cada lado é um
    /// elemento próprio, para que possam ser contados/editados separadamente.
    /// </summary>
    public class StiffenerBuilder
    {
        private readonly Document _doc;

        public StiffenerBuilder(Document doc) => _doc = doc;

        public void Build(StiffenerDefinition def, StiffenerPlacement placement,
                          StiffenerConfig config, ICollection<ElementId> createdIds = null)
        {
            double thickFt = config.PlateThickness / 304.8;
            var half   = placement.AxisDir * (thickFt / 2.0);
            var center = ResolveCenter(placement, thickFt, config.FlipAlignmentSide);

            foreach (var plate in def.Plates)
            {
                var loop = new CurveLoop();
                var pts  = new List<XYZ>();
                foreach (var (y, z) in plate.OutlineMm)
                    pts.Add(ToWorld(center, placement, y, z) - half);

                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    loop.Append(Line.CreateBound(a, b));
                }

                var solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { loop }, placement.AxisDir, thickFt);

                var shape = DirectShape.CreateElement(
                    _doc, new ElementId(BuiltInCategory.OST_StructuralStiffener));
                shape.SetShape(new GeometryObject[] { solid });
                shape.SetName($"Nervura {(plate.Side > 0 ? "A" : "B")} — {placement.BeamName}");
                createdIds?.Add(shape.Id);
            }
        }

        // Sem referência de alinhamento: chapa centrada no ponto clicado na viga
        // (comportamento de sempre). Com referência: a face mais próxima da
        // referência fica rente a ela — recalculado a cada Build a partir da
        // espessura ATUAL, então a face rente nunca se move quando a espessura
        // muda, só a face oposta (a que "cresce" para dentro do vão).
        private static XYZ ResolveCenter(StiffenerPlacement p, double thickFt, bool flip)
        {
            if (p.AlignFacePoint == null) return p.InsertionPoint;

            double targetT = (p.AlignFacePoint - p.InsertionPoint).DotProduct(p.AxisDir);
            double sign = targetT >= 0 ? 1.0 : -1.0;
            if (flip) sign = -sign;
            var facePoint = p.InsertionPoint + p.AxisDir * targetT;
            // O sinal estava invertido: a chapa nascia com a face errada rente
            // à referência, deslocada uma espessura inteira para fora do vão
            // (+metade de um lado em vez de -metade do outro = diferença de
            // uma espessura cheia). Corrigido invertendo o sentido do deslocamento.
            // "flip" é o ajuste manual — a heurística de sinal (targetT >= 0)
            // nem sempre acerta o lado certo dependendo de onde a referência
            // está em relação ao clique original na peça.
            return facePoint + p.AxisDir * (sign * thickFt / 2.0);
        }

        private static XYZ ToWorld(XYZ center, StiffenerPlacement p, double yMm, double zMm) =>
            center + p.Lateral * (yMm / 304.8) + p.Up * (zMm / 304.8);
    }
}
