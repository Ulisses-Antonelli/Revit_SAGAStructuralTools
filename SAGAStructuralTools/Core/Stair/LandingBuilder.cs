using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Models;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Cria os patamares (inferior, superior e intermediário) como DirectShape.
    /// Todos posicionados corretamente em qualquer direção da escada.
    /// </summary>
    public class LandingBuilder
    {
        private readonly Document _doc;

        public LandingBuilder(Document doc) => _doc = doc;

        /// <param name="startPt">Ponto de conexão na viga inferior.</param>
        /// <param name="horizDir">Direção horizontal unitária da escada.</param>
        /// <param name="lateral">Direção lateral unitária (perpendicular à marcha, plano XY).</param>
        public void Build(StairDefinition def, StairConfig config,
                          XYZ startPt, XYZ horizDir, XYZ lateral)
        {
            double lldFt   = def.LowerLandingDepth / 304.8;
            double uldFt   = def.UpperLandingDepth / 304.8;
            double illFt   = def.HasIntermediateLanding ? def.IntermediateLandingLength / 304.8 : 0;
            double halfWFt = config.Width / 2.0 / 304.8;
            double thickFt = config.TreadThickness / 304.8;
            double rFt     = def.RiserHeight / 304.8;
            double tFt     = def.TreadDepth  / 304.8;

            var stringerBottom = startPt + horizDir * lldFt;

            // Patamar inferior
            if (lldFt > 0.001)
                CreateLanding(startPt, lldFt, halfWFt, thickFt, horizDir, lateral, "Patamar_Inferior");

            // Patamar intermediário
            if (def.HasIntermediateLanding && illFt > 0.001)
            {
                int k = def.IntermediateLandingStep - 1;  // 0-indexed: último degrau da marcha inferior
                var midOrigin = new XYZ(
                    stringerBottom.X + horizDir.X * (k + 1) * tFt,
                    stringerBottom.Y + horizDir.Y * (k + 1) * tFt,
                    stringerBottom.Z + (k + 1) * rFt);
                CreateLanding(midOrigin, illFt, halfWFt, thickFt, horizDir, lateral, "Patamar_Intermediario");
            }

            // Patamar superior
            if (uldFt > 0.001)
            {
                double totalRunFt = def.TotalRun / 304.8;
                var upperOrigin = new XYZ(
                    stringerBottom.X + horizDir.X * (totalRunFt + illFt),
                    stringerBottom.Y + horizDir.Y * (totalRunFt + illFt),
                    startPt.Z + def.TotalRise / 304.8);
                CreateLanding(upperOrigin, uldFt, halfWFt, thickFt, horizDir, lateral, "Patamar_Superior");
            }
        }

        private void CreateLanding(XYZ origin, double depthFt, double halfWidthFt,
                                   double thicknessFt, XYZ horizDir, XYZ lateral, string name)
        {
            // Loop CCW visto de +Z: →horizDir, →lateral, ←horizDir, ←lateral
            var p0 = origin + lateral * (-halfWidthFt);
            var p1 = p0     + horizDir * depthFt;
            var p2 = p1     + lateral  * (2 * halfWidthFt);
            var p3 = p0     + lateral  * (2 * halfWidthFt);

            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(p0, p1));
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p0));

            var solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop }, XYZ.BasisZ, thicknessFt);

            var shape = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
            shape.SetShape(new GeometryObject[] { solid });
            shape.SetName(name);
        }
    }
}
