using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Cria os degraus como DirectShape (bloco geométrico BIM quantificável).
    /// Por padrão: retângulo 250 × Largura × 40 mm.
    /// Mantém categoria OST_GenericModel para preservar quantitativo BIM.
    /// </summary>
    public class TreadBuilder
    {
        private readonly Document _doc;

        public TreadBuilder(Document doc)
        {
            _doc = doc;
        }

        public void Build(StairDefinition def, StairConfig config, XYZ origin)
        {
            for (int i = 0; i < def.StepCount; i++)
            {
                var treadOrigin = new XYZ(
                    origin.X + (i * def.TreadDepth) / 304.8,   // posição horizontal (mm→ft)
                    origin.Y - (config.Width / 2.0) / 304.8,   // lado esquerdo
                    origin.Z + (i * def.RiserHeight) / 304.8); // altura do degrau

                CreateTreadShape(treadOrigin, config);
            }
        }

        private void CreateTreadShape(XYZ origin, StairConfig config)
        {
            var treadFt  = config.TreadDepth     / 304.8;
            var widthFt  = config.Width          / 304.8;
            var thickFt  = config.TreadThickness / 304.8;

            // Sólido: extrude o perfil retangular
            var profile = new List<CurveLoop> { RectProfile(origin, treadFt, widthFt) };
            var solid   = GeometryCreationUtilities.CreateExtrusionGeometry(
                profile, XYZ.BasisZ, thickFt);

            var shape = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
            shape.SetShape(new GeometryObject[] { solid });
            shape.SetName($"Degrau_{DateTime.Now.Ticks}");
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
