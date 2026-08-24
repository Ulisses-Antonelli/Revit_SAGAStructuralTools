using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Core.EndPlate
{
    /// <summary>
    /// Constrói cada chapa de topo como um <see cref="DirectShape"/> individual —
    /// mesmo idioma de <see cref="Stiffener.StiffenerBuilder"/>. No Modo 2 com
    /// as duas peças habilitadas, cada peça recebe sua própria chapa (nunca uma
    /// só fundida cobrindo as duas).
    /// </summary>
    public class EndPlateBuilder
    {
        private readonly Document _doc;

        public EndPlateBuilder(Document doc) => _doc = doc;

        public void Build(EndPlatePlacement placement, EndPlateConfig config,
                          ICollection<ElementId> createdIds = null)
        {
            var targets = config.GenerateBothMembers
                ? placement.Members
                : placement.Members.Take(1);

            // Modo 2: as duas peças são resolvidas de forma independente e, na
            // prática, seus pontos de extremidade quase nunca coincidem
            // exatamente (folga/sobreposição real do modelo). Por isso as duas
            // chapas usam a MESMA âncora — o meio da distância entre as duas
            // extremidades — em vez do ponto de cada uma, senão elas não ficam
            // faceando uma a outra. Se as peças já estiverem encostadas, os dois
            // pontos praticamente coincidem e o meio cai no mesmo lugar de antes.
            XYZ sharedAnchor = placement.Members.Count == 2
                ? (placement.Members[0].EndPoint + placement.Members[1].EndPoint) / 2.0
                : null;
            double gapFt = config.GapBetweenPlates / 304.8;

            foreach (var member in targets)
            {
                var def = EndPlateCalculator.Calculate(member.HeightMm, member.WidthMm, config);
                if (!def.IsValid)
                    throw new InvalidOperationException(
                        $"Chapa inválida para '{member.Name}': {string.Join(" ", def.Warnings)}");

                var anchor = sharedAnchor == null
                    ? member.EndPoint
                    : sharedAnchor + member.AxisDir * (gapFt / 2.0);

                BuildPlate(member, def, anchor, config, createdIds);
            }
        }

        private void BuildPlate(EndPlateMemberEnd member, EndPlateDefinition def, XYZ anchor,
                                EndPlateConfig config, ICollection<ElementId> createdIds)
        {
            double thickFt = config.PlateThickness / 304.8;
            double halfWFt = def.WidthMm  / 2.0 / 304.8;
            double halfHFt = def.HeightMm / 2.0 / 304.8;

            var w = member.WidthDir;
            var h = member.HeightDir;
            var origin = anchor;

            var pts = new List<XYZ>
            {
                origin + w * -halfWFt + h * -halfHFt,
                origin + w * -halfWFt + h * +halfHFt,
                origin + w * +halfWFt + h * +halfHFt,
                origin + w * +halfWFt + h * -halfHFt,
            };

            // WidthDir × HeightDir aponta no sentido "positivo" original do eixo
            // da peça; quando a chapa fica na extremidade OPOSTA (AxisDir
            // invertido), o laço precisa inverter a ordem pra continuar CCW em
            // relação à extrusão real (mesma correção de espelhamento já usada
            // no StiffenerCalculator).
            if (w.CrossProduct(h).DotProduct(member.AxisDir) < 0)
                pts.Reverse();

            var loop = new CurveLoop();
            for (int i = 0; i < pts.Count; i++)
                loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));

            var solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop }, member.AxisDir, thickFt);

            var shape = DirectShape.CreateElement(
                _doc, new ElementId(BuiltInCategory.OST_StructuralStiffener));
            shape.SetShape(new GeometryObject[] { solid });
            shape.SetName($"Chapa de Topo — {member.Name}");
            createdIds?.Add(shape.Id);
        }
    }
}
