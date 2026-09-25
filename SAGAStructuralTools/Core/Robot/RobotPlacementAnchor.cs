using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Domain;
using System;

namespace SAGAStructuralTools.Core.Robot
{
    /// <summary>
    /// Converte coordenadas cruas do Robot (na unidade do arquivo, ex.: metros) para o sistema do
    /// Revit (feet), usando um nó de referência escolhido pelo usuário como âncora — sem isso, o
    /// modelo nasceria nas coordenadas absolutas e arbitrárias do Robot, sem nenhuma relação com o
    /// modelo real do Revit ("perdido no espaço"). Mesmo princípio já usado no bridge do Navisworks:
    /// ponto de referência + ponto de direção opcional definem translação e rotação em Z.
    /// </summary>
    public class RobotPlacementAnchor
    {
        private readonly double _toFeet;
        private readonly Transform _rotation;
        private readonly XYZ _originOffset;
        public double RotationDegrees { get; }

        public RobotPlacementAnchor(
            RobotNode anchorNode, XYZ anchorRevitPoint, double lengthUnitToFeet,
            RobotNode directionNode = null, XYZ directionRevitPoint = null)
        {
            _toFeet = lengthUnitToFeet;

            double angleRad = 0;
            if (directionNode != null && directionRevitPoint != null)
                angleRad = ComputeZAngle(anchorNode, directionNode, anchorRevitPoint, directionRevitPoint);
            RotationDegrees = angleRad * 180.0 / Math.PI;

            _rotation = Transform.CreateRotation(XYZ.BasisZ, angleRad);
            var anchorRawFt = RawToFeet(anchorNode);
            var rotatedAnchorFt = _rotation.OfPoint(anchorRawFt);
            _originOffset = anchorRevitPoint - rotatedAnchorFt;
        }

        public XYZ ToRevit(RobotNode node) => _rotation.OfPoint(RawToFeet(node)) + _originOffset;

        public static double LengthUnitToFeet(string unit)
        {
            switch ((unit ?? "m").Trim().ToLowerInvariant())
            {
                case "m": return UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Meters);
                case "cm": return UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Centimeters);
                case "mm": return UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
                case "ft": return 1.0;
                default:
                    throw new InvalidOperationException(
                        $"Unidade de comprimento '{unit}' do arquivo do Robot não reconhecida (esperava m, cm, mm ou ft).");
            }
        }

        private XYZ RawToFeet(RobotNode node) => new XYZ(node.X * _toFeet, node.Y * _toFeet, node.Z * _toFeet);

        private static double ComputeZAngle(RobotNode anchorNode, RobotNode directionNode, XYZ anchorRevitPoint, XYZ directionRevitPoint)
        {
            double robotDx = directionNode.X - anchorNode.X;
            double robotDy = directionNode.Y - anchorNode.Y;
            var revitDelta = directionRevitPoint - anchorRevitPoint;

            double robotPlanar = Math.Sqrt(robotDx * robotDx + robotDy * robotDy);
            double revitPlanar = Math.Sqrt(revitDelta.X * revitDelta.X + revitDelta.Y * revitDelta.Y);
            if (robotPlanar < 1e-9 || revitPlanar < 1e-9) return 0;

            return Math.Atan2(revitDelta.Y, revitDelta.X) - Math.Atan2(robotDy, robotDx);
        }
    }
}
