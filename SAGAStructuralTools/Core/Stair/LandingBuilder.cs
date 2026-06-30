using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Models;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Cria os patamares (inferior, superior e intermediário) como DirectShape.
    /// </summary>
    public class LandingBuilder
    {
        private readonly Document _doc;

        public LandingBuilder(Document doc)
        {
            _doc = doc;
        }

        public void Build(StairDefinition def, StairConfig config, XYZ origin)
        {
            // Patamar inferior
            if (def.LowerLandingDepth > 0)
                CreateLanding(origin, def.LowerLandingDepth, config.Width, config.TreadThickness, "Patamar_Inferior");

            // Patamar superior
            if (def.UpperLandingDepth > 0)
            {
                var upperOrigin = new XYZ(
                    origin.X + (def.LowerLandingDepth + def.TotalRun) / 304.8,
                    origin.Y,
                    origin.Z + def.TotalRise / 304.8);
                CreateLanding(upperOrigin, def.UpperLandingDepth, config.Width, config.TreadThickness, "Patamar_Superior");
            }

            // Patamar intermediário
            if (def.HasIntermediateLanding && config.IntermediateLanding != LandingMode.Never)
            {
                var midRun  = (def.IntermediateLandingAt / def.TotalRise) * def.TotalRun;
                var midOrigin = new XYZ(
                    origin.X + (def.LowerLandingDepth + midRun) / 304.8,
                    origin.Y,
                    origin.Z + def.IntermediateLandingAt / 304.8);
                CreateLanding(midOrigin, def.TreadDepth, config.Width, config.TreadThickness, "Patamar_Intermediario");
            }
        }

        private void CreateLanding(XYZ origin, double depthMm, double widthMm, double thicknessMm, string name)
        {
            var dFt = depthMm    / 304.8;
            var wFt = widthMm   / 304.8;
            var tFt = thicknessMm / 304.8;

            var profile = new List<CurveLoop> { RectProfile(origin, dFt, wFt) };
            var solid   = GeometryCreationUtilities.CreateExtrusionGeometry(profile, XYZ.BasisZ, tFt);

            var shape = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
            shape.SetShape(new GeometryObject[] { solid });
            shape.SetName(name);
        }

        private static CurveLoop RectProfile(XYZ origin, double depth, double width)
        {
            var p0 = origin;
            var p1 = origin + new XYZ(depth, 0,     0);
            var p2 = origin + new XYZ(depth, width, 0);
            var p3 = origin + new XYZ(0,     width, 0);
            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(p0, p1));
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p0));
            return loop;
        }
    }
}
