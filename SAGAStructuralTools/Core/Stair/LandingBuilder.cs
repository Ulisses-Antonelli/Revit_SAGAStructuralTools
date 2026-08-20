using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Models;
using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Cria o patamar intermediário como DirectShape (chapa horizontal no meio do vão).
    /// Patamares inicial e superior são omitidos — a geometria estrutural (beams) é
    /// gerada pelo StringerBuilder; aqui só a chapa de piso do patamar intermediário.
    ///
    /// Posicionamento:
    ///   início  = focinho do degrau i=k  → (k+2)·tFt desde stringerBottom
    ///   fim     = frente do degrau i=k+1 → (k+2)·tFt + ILL
    ///   depth   = ILL  (vão livre útil exato conforme configuração)
    ///   Z top   = (k+1)·riserFt desde stringerBottom.Z  (mesmo nível do degrau)
    /// </summary>
    public class LandingBuilder
    {
        private readonly Document _doc;

        public LandingBuilder(Document doc) => _doc = doc;

        public void Build(StairDefinition def, StairConfig config,
                          XYZ startPt, XYZ horizDir, XYZ lateral,
                          ICollection<ElementId> createdIds = null)
        {
            if (!def.HasIntermediateLanding) return;

            double lldFt   = def.LowerLandingDepth / 304.8;
            double illFt   = def.IntermediateLandingLength / 304.8;
            double halfWFt = config.Width / 2.0 / 304.8;
            double thickFt = config.TreadThickness / 304.8;
            double rFt     = def.RiserHeight / 304.8;
            double tFt     = def.TreadDepth  / 304.8;

            if (illFt <= 0.001) return;

            var stringerBottom = startPt + horizDir * lldFt;

            int k = def.IntermediateLandingStep - 1;  // 0-indexed: último degrau da marcha inferior
            var midOrigin = new XYZ(
                stringerBottom.X + horizDir.X * (k + 2) * tFt,
                stringerBottom.Y + horizDir.Y * (k + 2) * tFt,
                stringerBottom.Z + (k + 1) * rFt - thickFt);

            CreateSlab(midOrigin, illFt, halfWFt, thickFt, horizDir, lateral, createdIds);
        }

        private void CreateSlab(XYZ origin, double depthFt, double halfWidthFt,
                                 double thicknessFt, XYZ horizDir, XYZ lateral,
                                 ICollection<ElementId> createdIds)
        {
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
            shape.SetName("Patamar_Intermediario");
            createdIds?.Add(shape.Id);
        }
    }
}
