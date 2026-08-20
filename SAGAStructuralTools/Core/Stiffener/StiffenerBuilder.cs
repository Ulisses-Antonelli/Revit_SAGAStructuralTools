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
            var half = placement.AxisDir * (thickFt / 2.0);

            foreach (var plate in def.Plates)
            {
                var loop = new CurveLoop();
                var pts  = new List<XYZ>();
                foreach (var (y, z) in plate.OutlineMm)
                    pts.Add(ToWorld(placement, y, z) - half);

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

        private static XYZ ToWorld(StiffenerPlacement p, double yMm, double zMm) =>
            p.InsertionPoint + p.Lateral * (yMm / 304.8) + p.Up * (zMm / 304.8);
    }
}
