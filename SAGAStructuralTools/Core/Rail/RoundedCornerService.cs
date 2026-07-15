using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core;
using System;
using System.Linq;

namespace SAGAStructuralTools.Core.Rail
{
    internal sealed class RoundedCornerResult
    {
        internal ElementId CurvedElementId { get; set; }
        internal XYZ Vertex { get; set; }
        internal XYZ FirstTangent { get; set; }
        internal XYZ SecondTangent { get; set; }
        internal double RadiusMm { get; set; }
        internal double TurnAngleDegrees { get; set; }
    }

    /// <summary>
    /// Encurta ou estende dois membros retos até os pontos de tangência e cria um
    /// terceiro perfil curvo entre eles. Aceita duas vigas ou uma viga e um pilar.
    /// </summary>
    internal static class RoundedCornerService
    {
        private const double MillimetersPerFoot = 304.8;
        private const double AxisIntersectionToleranceMm = 0.5;
        private const double MinimumAngleDegrees = 5.0;
        private const double MaximumAngleDegrees = 175.0;
        private const double MaximumAutomaticEndpointAdjustmentMm = 1000.0;

        internal static void ValidateSelection(
            Document document,
            ElementId firstId,
            ElementId secondId)
        {
            ValidateRequest(
                document,
                firstId,
                secondId,
                0.1,
                0.0,
                false);
        }

        internal static void ValidateRadius(
            Document document,
            ElementId firstId,
            ElementId secondId,
            double radiusMm)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            ValidateRequest(
                document,
                firstId,
                secondId,
                radiusMm,
                document.Application.ShortCurveTolerance,
                true);
        }

        private static void ValidateRequest(
            Document document,
            ElementId firstId,
            ElementId secondId,
            double radiusMm,
            double shortCurveTolerance,
            bool validateExtension)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (radiusMm <= 0 || double.IsNaN(radiusMm) || double.IsInfinity(radiusMm))
                throw new InvalidOperationException("Informe um raio maior que zero.");

            var first = RoundedCornerMember.Get(document, firstId, "primeiro");
            var second = RoundedCornerMember.Get(document, secondId, "segundo");
            ValidatePair(first, second);

            var firstLine = first.GetAxis();
            var secondLine = second.GetAxis();
            var solution = Calculate(
                firstLine,
                secondLine,
                radiusMm / MillimetersPerFoot,
                shortCurveTolerance,
                validateExtension,
                GetVertexAnchor(first, second));
            first.EnsureCornerEndEditable(solution.FirstCornerEnd);
            second.EnsureCornerEndEditable(solution.SecondCornerEnd);
            RoundedCornerStore.EnsureEndpointsAreAvailable(
                document,
                first.Instance,
                solution.FirstCornerEnd,
                second.Instance,
                solution.SecondCornerEnd);
        }

        internal static RoundedCornerResult Apply(
            Document document,
            ElementId firstId,
            ElementId secondId,
            double radiusMm)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (radiusMm <= 0 || double.IsNaN(radiusMm) || double.IsInfinity(radiusMm))
                throw new InvalidOperationException("Informe um raio maior que zero.");

            var first = RoundedCornerMember.Get(document, firstId, "primeiro");
            var second = RoundedCornerMember.Get(document, secondId, "segundo");
            ValidatePair(first, second);

            var firstLine = first.GetAxis();
            var secondLine = second.GetAxis();

            var solution = Calculate(
                firstLine,
                secondLine,
                radiusMm / MillimetersPerFoot,
                document.Application.ShortCurveTolerance,
                true,
                GetVertexAnchor(first, second));

            first.EnsureCornerEndEditable(solution.FirstCornerEnd);
            second.EnsureCornerEndEditable(solution.SecondCornerEnd);
            RoundedCornerStore.EnsureEndpointsAreAvailable(
                document,
                first.Instance,
                solution.FirstCornerEnd,
                second.Instance,
                solution.SecondCornerEnd);

            bool firstJoinWasAllowed = first.IsJoinAllowedAtEnd(solution.FirstCornerEnd);
            bool secondJoinWasAllowed = second.IsJoinAllowedAtEnd(solution.SecondCornerEnd);
            first.DisallowJoinAtEnd(solution.FirstCornerEnd);
            second.DisallowJoinAtEnd(solution.SecondCornerEnd);

            first.SetCornerEndpoint(solution.FirstCornerEnd, solution.FirstTangent);
            second.SetCornerEndpoint(solution.SecondCornerEnd, solution.SecondTangent);
            document.Regenerate();
            EnsureEndpointWasApplied(
                first,
                solution.FirstCornerEnd,
                solution.FirstTangent);
            EnsureEndpointWasApplied(
                second,
                solution.SecondCornerEnd,
                solution.SecondTangent);

            var beam = first.IsBeam ? first : second;
            var symbol = beam.Instance.Symbol;
            if (!symbol.IsActive) symbol.Activate();

            var level = GetReferenceLevel(document, beam.Instance, solution.Vertex.Z);
            var arc = Arc.Create(
                solution.FirstTangent,
                solution.SecondTangent,
                solution.PointOnArc);

            var curved = document.Create.NewFamilyInstance(
                arc,
                symbol,
                level,
                StructuralType.Beam);
            if (curved == null)
                throw new InvalidOperationException(
                    "A família selecionada não aceitou a criação sobre um arco.");

            CopyPlacementParameters(beam.Instance, curved);
            document.Regenerate();

            StructuralFramingUtils.DisallowJoinAtEnd(curved, 0);
            StructuralFramingUtils.DisallowJoinAtEnd(curved, 1);

            // Reaplica o arco depois de desabilitar as juntas para neutralizar qualquer
            // ajuste automático de cutback feito durante a criação da viga.
            if (curved.Location is LocationCurve curvedLocation)
                curvedLocation.Curve = arc;

            RoundedCornerStore.RegisterIfNeeded(
                document,
                first,
                firstLine,
                solution.FirstCornerEnd,
                firstJoinWasAllowed,
                second,
                secondLine,
                solution.SecondCornerEnd,
                secondJoinWasAllowed,
                curved,
                radiusMm);

            return new RoundedCornerResult
            {
                CurvedElementId = curved.Id,
                Vertex = solution.Vertex,
                FirstTangent = solution.FirstTangent,
                SecondTangent = solution.SecondTangent,
                RadiusMm = radiusMm,
                TurnAngleDegrees = (Math.PI - solution.RayAngleRadians) * 180.0 / Math.PI
            };
        }

        private static void ValidatePair(
            RoundedCornerMember first,
            RoundedCornerMember second)
        {
            if (first.Instance.Id.Equals(second.Instance.Id))
                throw new InvalidOperationException("Selecione dois membros diferentes.");

            if (!first.IsBeam && !second.IsBeam)
            {
                throw new InvalidOperationException(
                    "Selecione ao menos uma viga. O trecho curvo é criado com o tipo da viga.");
            }

            if (first.IsBeam != second.IsBeam)
            {
                var beam = first.IsBeam ? first.Instance : second.Instance;
                EnsureUniformCenterJustification(beam);
                EnsureZeroDoubleParameter(
                    beam,
                    BuiltInParameter.Y_OFFSET_VALUE,
                    "deslocamento lateral");
                EnsureZeroDoubleParameter(
                    beam,
                    BuiltInParameter.Z_OFFSET_VALUE,
                    "deslocamento vertical");
                return;
            }

            var firstBeam = first.Instance;
            var secondBeam = second.Instance;
            if (firstBeam.Symbol == null || secondBeam.Symbol == null ||
                !firstBeam.Symbol.Id.Equals(secondBeam.Symbol.Id))
            {
                throw new InvalidOperationException(
                    "Os dois perfis precisam usar a mesma família e o mesmo tipo.");
            }

            EnsureSameDoubleParameter(
                firstBeam,
                secondBeam,
                BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE,
                "rotação do corte transversal");
            EnsureSameIntegerParameter(
                firstBeam,
                secondBeam,
                BuiltInParameter.YZ_JUSTIFICATION,
                "modo de justificação");
            EnsureSameIntegerParameter(
                firstBeam,
                secondBeam,
                BuiltInParameter.Y_JUSTIFICATION,
                "justificação lateral");
            EnsureSameIntegerParameter(
                firstBeam,
                secondBeam,
                BuiltInParameter.Z_JUSTIFICATION,
                "justificação vertical");
            EnsureUniformCenterJustification(firstBeam);
            EnsureUniformCenterJustification(secondBeam);
            EnsureSameDoubleParameter(
                firstBeam,
                secondBeam,
                BuiltInParameter.Y_OFFSET_VALUE,
                "deslocamento lateral");
            EnsureZeroDoubleParameter(
                firstBeam,
                BuiltInParameter.Y_OFFSET_VALUE,
                "deslocamento lateral");
            EnsureZeroDoubleParameter(
                secondBeam,
                BuiltInParameter.Y_OFFSET_VALUE,
                "deslocamento lateral");
            EnsureSameDoubleParameter(
                firstBeam,
                secondBeam,
                BuiltInParameter.Z_OFFSET_VALUE,
                "deslocamento vertical");
        }

        private static int GetVertexAnchor(
            RoundedCornerMember first,
            RoundedCornerMember second)
        {
            if (first.Kind == RoundedCornerMemberKind.VerticalPointColumn) return 0;
            if (second.Kind == RoundedCornerMemberKind.VerticalPointColumn) return 1;
            return -1;
        }

        private static void EnsureEndpointWasApplied(
            RoundedCornerMember member,
            int cornerEnd,
            XYZ expectedPoint)
        {
            double toleranceFt = Math.Max(
                0.1 / MillimetersPerFoot,
                member.Instance.Document.Application.VertexTolerance);
            double differenceFt = member.GetAxis()
                .GetEndPoint(cornerEnd)
                .DistanceTo(expectedPoint);
            if (differenceFt > toleranceFt)
            {
                throw new InvalidOperationException(
                    $"O Revit não posicionou a extremidade do perfil na tangência " +
                    $"({differenceFt * MillimetersPerFoot:F1} mm de diferença). " +
                    "Verifique as restrições do elemento.");
            }
        }

        private static CornerSolution Calculate(
            Line first,
            Line second,
            double radiusFt,
            double shortCurveTolerance,
            bool validateExtension,
            int vertexAnchor)
        {
            var p0 = first.GetEndPoint(0);
            var p1 = first.GetEndPoint(1);
            var q0 = second.GetEndPoint(0);
            var q1 = second.GetEndPoint(1);

            var firstDirection = Direction(p0, p1, "primeiro");
            var secondDirection = Direction(q0, q1, "segundo");
            double directionDot = Math.Max(
                -1.0,
                Math.Min(1.0, firstDirection.DotProduct(secondDirection)));
            double denominator = 1.0 - directionDot * directionDot;
            if (denominator < 1e-12)
                throw new InvalidOperationException(
                    "Os perfis são paralelos ou quase paralelos e não formam um canto válido.");

            // Pontos mais próximos das duas retas infinitas. Quando a distância entre
            // eles é praticamente zero, as retas são coplanares e possuem um vértice.
            var delta = p0 - q0;
            double firstDeltaProjection = firstDirection.DotProduct(delta);
            double secondDeltaProjection = secondDirection.DotProduct(delta);
            double firstParameter =
                (directionDot * secondDeltaProjection - firstDeltaProjection) /
                denominator;
            double secondParameter =
                (secondDeltaProjection -
                 directionDot * firstDeltaProjection) /
                denominator;
            var firstIntersection = p0 + firstDirection * firstParameter;
            var secondIntersection = q0 + secondDirection * secondParameter;
            double axesDistance = firstIntersection.DistanceTo(secondIntersection);
            double axesTolerance = AxisIntersectionToleranceMm / MillimetersPerFoot;
            if (axesDistance > axesTolerance)
            {
                throw new InvalidOperationException(
                    $"Os eixos dos dois membros não se encontram " +
                    $"({axesDistance * MillimetersPerFoot:F1} mm de afastamento). " +
                    "Alinhe os eixos antes de arredondar o canto.");
            }

            var vertex = vertexAnchor == 0
                ? firstIntersection
                : vertexAnchor == 1
                    ? secondIntersection
                    : (firstIntersection + secondIntersection) * 0.5;

            int firstCornerEnd = NearestEnd(first, vertex);
            int secondCornerEnd = NearestEnd(second, vertex);
            var firstNear = first.GetEndPoint(firstCornerEnd);
            var secondNear = second.GetEndPoint(secondCornerEnd);
            var firstFar = first.GetEndPoint(1 - firstCornerEnd);
            var secondFar = second.GetEndPoint(1 - secondCornerEnd);

            var firstRay = Direction(vertex, firstFar, "primeiro");
            var secondRay = Direction(vertex, secondFar, "segundo");
            double firstNearAlong = (firstNear - vertex).DotProduct(firstRay);
            double secondNearAlong = (secondNear - vertex).DotProduct(secondRay);
            ValidateInternalIntersection(firstNearAlong, first, "primeiro");
            ValidateInternalIntersection(secondNearAlong, second, "segundo");
            double dot = Math.Max(-1.0, Math.Min(1.0, firstRay.DotProduct(secondRay)));
            double rayAngle = Math.Acos(dot);
            double rayAngleDegrees = rayAngle * 180.0 / Math.PI;

            if (rayAngleDegrees < MinimumAngleDegrees ||
                rayAngleDegrees > MaximumAngleDegrees)
            {
                throw new InvalidOperationException(
                    $"O ângulo entre os perfis ({rayAngleDegrees:F1} graus) não permite um arco estável. " +
                    $"Use um ângulo entre {MinimumAngleDegrees:F0} e {MaximumAngleDegrees:F0} graus.");
            }

            double tangentDistance = radiusFt / Math.Tan(rayAngle / 2.0);
            var firstTangent = vertex + firstRay * tangentDistance;
            var secondTangent = vertex + secondRay * tangentDistance;
            if (validateExtension)
            {
                ValidateEndpointAdjustment(
                    firstNear,
                    firstTangent,
                    firstNearAlong,
                    tangentDistance,
                    "primeiro");
                ValidateEndpointAdjustment(
                    secondNear,
                    secondTangent,
                    secondNearAlong,
                    tangentDistance,
                    "segundo");
            }

            double firstAvailable = vertex.DistanceTo(firstFar);
            double secondAvailable = vertex.DistanceTo(secondFar);
            double required = tangentDistance + shortCurveTolerance;
            if (firstAvailable <= required || secondAvailable <= required)
            {
                double maxRadiusFt = Math.Min(firstAvailable, secondAvailable) *
                                     Math.Tan(rayAngle / 2.0);
                double maxRadiusMm = Math.Max(0, maxRadiusFt * MillimetersPerFoot);
                throw new InvalidOperationException(
                    $"O raio de {radiusFt * MillimetersPerFoot:F1} mm não cabe nos perfis selecionados. " +
                    $"Use um raio menor que aproximadamente {maxRadiusMm:F1} mm.");
            }

            var bisector = firstRay + secondRay;
            if (bisector.GetLength() < 1e-9)
                throw new InvalidOperationException("Não foi possível determinar o centro do arco.");

            double centerDistance = radiusFt / Math.Sin(rayAngle / 2.0);
            var center = vertex + bisector.Normalize() * centerDistance;
            var firstRadial = (firstTangent - center).Normalize();
            var secondRadial = (secondTangent - center).Normalize();
            var middleRadial = firstRadial + secondRadial;
            if (middleRadial.GetLength() < 1e-9)
                throw new InvalidOperationException("O arco calculado é geometricamente ambíguo.");

            var pointOnArc = center + middleRadial.Normalize() * radiusFt;
            double arcLength = radiusFt * (Math.PI - rayAngle);
            if (arcLength <= shortCurveTolerance)
                throw new InvalidOperationException(
                    "O arco calculado é menor que a tolerância mínima do Revit.");

            return new CornerSolution
            {
                Vertex = vertex,
                FirstCornerEnd = firstCornerEnd,
                SecondCornerEnd = secondCornerEnd,
                FirstTangent = firstTangent,
                SecondTangent = secondTangent,
                PointOnArc = pointOnArc,
                RayAngleRadians = rayAngle
            };
        }

        private static void ValidateInternalIntersection(
            double nearAlongFt,
            Line line,
            string label)
        {
            if (nearAlongFt >= 0) return;

            double lineLengthFt = line.Length;
            double maximumPenetrationFt = Math.Min(
                MaximumAutomaticEndpointAdjustmentMm / MillimetersPerFoot,
                lineLengthFt * 0.35);
            double penetrationFt = -nearAlongFt;
            if (penetrationFt > maximumPenetrationFt + 1e-9)
            {
                throw new InvalidOperationException(
                    $"A interseção está {penetrationFt * MillimetersPerFoot:F0} mm " +
                    $"para dentro do {label} perfil. Selecione uma extremidade mais próxima.");
            }
        }

        private static void ValidateEndpointAdjustment(
            XYZ nearPoint,
            XYZ tangentPoint,
            double nearAlongFt,
            double tangentDistanceFt,
            string label)
        {
            double adjustmentFt = nearPoint.DistanceTo(tangentPoint);
            double maximumFt = MaximumAutomaticEndpointAdjustmentMm / MillimetersPerFoot;
            if (adjustmentFt > maximumFt + 1e-9)
            {
                string operation = nearAlongFt > tangentDistanceFt
                    ? "prolongado"
                    : "recortado";
                throw new InvalidOperationException(
                    $"O {label} perfil precisaria ser {operation} em " +
                    $"{adjustmentFt * MillimetersPerFoot:F0} mm para alcançar a tangência. " +
                    $"O limite automático é {MaximumAutomaticEndpointAdjustmentMm:F0} mm.");
            }
        }

        private static XYZ Direction(XYZ start, XYZ end, string label)
        {
            var vector = end - start;
            if (vector.GetLength() < 1e-9)
                throw new InvalidOperationException(
                    $"O {label} perfil não possui comprimento válido.");
            return vector.Normalize();
        }

        private static int NearestEnd(Line line, XYZ point)
        {
            return line.GetEndPoint(0).DistanceTo(point) <=
                   line.GetEndPoint(1).DistanceTo(point)
                ? 0
                : 1;
        }

        private static Level GetReferenceLevel(
            Document document,
            FamilyInstance source,
            double elevationFt)
        {
            var level = document.GetElement(source.LevelId) as Level;
            if (level != null) return level;

            var referenceLevel = source.get_Parameter(
                BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
            if (referenceLevel != null &&
                referenceLevel.StorageType == StorageType.ElementId)
            {
                var referenceLevelId = referenceLevel.AsElementId();
                if (referenceLevelId != null &&
                    !referenceLevelId.Equals(ElementId.InvalidElementId))
                {
                    level = document.GetElement(referenceLevelId) as Level;
                    if (level != null) return level;
                }
            }

            return new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(candidate => Math.Abs(candidate.ProjectElevation - elevationFt))
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "Nenhum nível foi encontrado para criar o perfil curvo.");
        }

        private static void CopyPlacementParameters(
            FamilyInstance source,
            FamilyInstance target)
        {
            CopyParameter(source, target, BuiltInParameter.YZ_JUSTIFICATION);
            CopyParameter(source, target, BuiltInParameter.Y_JUSTIFICATION);
            CopyParameter(source, target, BuiltInParameter.Z_JUSTIFICATION);
            CopyParameter(source, target, BuiltInParameter.Y_OFFSET_VALUE);
            CopyParameter(source, target, BuiltInParameter.Z_OFFSET_VALUE);
            CopyParameter(source, target, BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE);
        }

        private static void EnsureUniformCenterJustification(FamilyInstance instance)
        {
            var yz = instance.get_Parameter(BuiltInParameter.YZ_JUSTIFICATION);
            if (yz != null && yz.StorageType == StorageType.Integer && yz.AsInteger() != 0)
            {
                throw new InvalidOperationException(
                    "O arredondamento aceita somente perfis com justificação YZ uniforme.");
            }

            var y = instance.get_Parameter(BuiltInParameter.Y_JUSTIFICATION);
            if (y != null && y.StorageType == StorageType.Integer &&
                y.AsInteger() != (int)YJustification.Center)
            {
                throw new InvalidOperationException(
                    "O arredondamento aceita somente perfis justificados ao centro no eixo Y.");
            }

            var z = instance.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
            if (z != null && z.StorageType == StorageType.Integer &&
                z.AsInteger() != (int)ZJustification.Center)
            {
                throw new InvalidOperationException(
                    "O arredondamento aceita somente perfis justificados ao centro no eixo Z.");
            }
        }

        private static void CopyParameter(
            Element source,
            Element target,
            BuiltInParameter parameterId)
        {
            var sourceParameter = source.get_Parameter(parameterId);
            var targetParameter = target.get_Parameter(parameterId);
            if (sourceParameter == null || targetParameter == null || targetParameter.IsReadOnly)
                return;

            switch (sourceParameter.StorageType)
            {
                case StorageType.Double:
                    targetParameter.Set(sourceParameter.AsDouble());
                    break;
                case StorageType.Integer:
                    targetParameter.Set(sourceParameter.AsInteger());
                    break;
            }
        }

        private static void EnsureSameDoubleParameter(
            Element first,
            Element second,
            BuiltInParameter parameterId,
            string label)
        {
            var firstParameter = first.get_Parameter(parameterId);
            var secondParameter = second.get_Parameter(parameterId);
            if (firstParameter == null || secondParameter == null ||
                firstParameter.StorageType != StorageType.Double ||
                secondParameter.StorageType != StorageType.Double)
                return;

            if (Math.Abs(firstParameter.AsDouble() - secondParameter.AsDouble()) > 1e-8)
                throw new InvalidOperationException(
                    $"Os dois perfis precisam ter a mesma {label}.");
        }

        private static void EnsureSameIntegerParameter(
            Element first,
            Element second,
            BuiltInParameter parameterId,
            string label)
        {
            var firstParameter = first.get_Parameter(parameterId);
            var secondParameter = second.get_Parameter(parameterId);
            if (firstParameter == null || secondParameter == null ||
                firstParameter.StorageType != StorageType.Integer ||
                secondParameter.StorageType != StorageType.Integer)
                return;

            if (firstParameter.AsInteger() != secondParameter.AsInteger())
                throw new InvalidOperationException(
                    $"Os dois perfis precisam ter o mesmo {label}.");
        }

        private static void EnsureZeroDoubleParameter(
            Element element,
            BuiltInParameter parameterId,
            string label)
        {
            var parameter = element.get_Parameter(parameterId);
            if (parameter != null &&
                parameter.StorageType == StorageType.Double &&
                Math.Abs(parameter.AsDouble()) > 0.1 / MillimetersPerFoot)
            {
                throw new InvalidOperationException(
                    $"O arredondamento aceita somente perfis com {label} igual a zero.");
            }
        }

        private sealed class CornerSolution
        {
            internal XYZ Vertex { get; set; }
            internal int FirstCornerEnd { get; set; }
            internal int SecondCornerEnd { get; set; }
            internal XYZ FirstTangent { get; set; }
            internal XYZ SecondTangent { get; set; }
            internal XYZ PointOnArc { get; set; }
            internal double RayAngleRadians { get; set; }
        }
    }
}
