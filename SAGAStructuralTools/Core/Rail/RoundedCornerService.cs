using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core;
using System;
using System.Linq;

namespace SAGAStructuralTools.Core.Rail
{
    internal sealed class RoundedCornerTransitionRequiredException : InvalidOperationException
    {
        internal RoundedCornerTransitionRequiredException(string message)
            : base(message)
        {
        }
    }

    internal sealed class RoundedCornerRequest
    {
        internal RoundedCornerRequest(
            ElementId firstId,
            ElementId secondId,
            int? firstCornerEnd = null,
            int? secondCornerEnd = null)
        {
            FirstId = firstId;
            SecondId = secondId;
            FirstCornerEnd = firstCornerEnd;
            SecondCornerEnd = secondCornerEnd;
        }

        internal ElementId FirstId { get; }
        internal ElementId SecondId { get; }
        internal int? FirstCornerEnd { get; }
        internal int? SecondCornerEnd { get; }
    }

    internal sealed class RoundedCornerPlan
    {
        internal ElementId FirstId { get; set; }
        internal ElementId SecondId { get; set; }
        internal Line OriginalFirstLine { get; set; }
        internal Line OriginalSecondLine { get; set; }
        internal int FirstCornerEnd { get; set; }
        internal int SecondCornerEnd { get; set; }
        internal XYZ Vertex { get; set; }
        internal XYZ FirstTangent { get; set; }
        internal XYZ SecondTangent { get; set; }
        internal XYZ PointOnArc { get; set; }
        internal double RadiusMm { get; set; }
        internal double RayAngleRadians { get; set; }
    }

    internal sealed class RoundedCornerCompoundPlan
    {
        internal RoundedCornerPlan FirstCorner { get; set; }
        internal RoundedCornerPlan SecondCorner { get; set; }
        internal ElementId MiddleId { get; set; }
    }

    internal sealed class RoundedCornerAutomaticCompoundPlan
    {
        internal ElementId FirstId { get; set; }
        internal ElementId SecondId { get; set; }
        internal int FirstCornerEnd { get; set; }
        internal int SecondCornerEnd { get; set; }
        internal Line MiddleAxis { get; set; }
        internal double RadiusMm { get; set; }
    }

    internal sealed class RoundedCornerAutomaticCompoundResult
    {
        internal ElementId MiddleElementId { get; set; }
        internal RoundedCornerResult[] CornerResults { get; set; }
    }

    internal sealed class RoundedCornerReferenceRoutePlan
    {
        internal ElementId InclinedId { get; set; }
        internal ElementId HorizontalId { get; set; }
        internal int InclinedCornerEnd { get; set; }
        internal int HorizontalCornerEnd { get; set; }
        internal Line ReferenceAxis { get; set; }
        internal Line LevelAxis { get; set; }
        internal Line ConnectorAxis { get; set; }
        internal double RadiusMm { get; set; }
    }

    internal sealed class RoundedCornerReferenceRouteResult
    {
        internal ElementId LevelElementId { get; set; }
        internal ElementId ConnectorElementId { get; set; }
        internal RoundedCornerResult[] CornerResults { get; set; }
    }

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

        internal static void ValidateSelection(
            Document document,
            RoundedCornerRequest request)
        {
            CreatePlan(document, request, 0.1, 0.0, false);
        }

        internal static void ValidateRadius(
            Document document,
            RoundedCornerRequest request,
            double radiusMm)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            CreatePlan(
                document,
                request,
                radiusMm,
                document.Application.ShortCurveTolerance,
                true);
        }

        internal static int ResolvePickedEnd(
            Document document,
            ElementId elementId,
            XYZ pickedPoint)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (pickedPoint == null)
                throw new InvalidOperationException(
                    "Não foi possível identificar o ponto clicado no corrimão.");
            if (!IsFinite(pickedPoint))
                throw new InvalidOperationException(
                    "O ponto clicado no corrimão não possui coordenadas válidas.");

            var member = RoundedCornerMember.Get(document, elementId, "selecionado");
            if (!member.IsBeam)
                throw new InvalidOperationException(
                    "A ferramenta Unir Corrimãos aceita somente vigas estruturais retas.");

            var line = member.GetAxis();
            var start = line.GetEndPoint(0);
            var end = line.GetEndPoint(1);

            // Em vistas de planta o Revit não garante que GlobalPoint.Z seja
            // significativo. A estação do clique deve, portanto, ser medida em XY.
            if (document.ActiveView is ViewPlan)
            {
                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double lengthSquared = dx * dx + dy * dy;
                if (lengthSquared < 1e-12)
                {
                    throw new InvalidOperationException(
                        "Não é possível escolher a extremidade deste perfil em planta. " +
                        "Use uma vista 3D ou de elevação.");
                }

                double station =
                    ((pickedPoint.X - start.X) * dx +
                     (pickedPoint.Y - start.Y) * dy) /
                    lengthSquared;
                return station <= 0.5 ? 0 : 1;
            }

            var direction = Direction(start, end, "selecionado");
            double along = (pickedPoint - start).DotProduct(direction);
            return along <= line.Length * 0.5 ? 0 : 1;
        }

        internal static RoundedCornerPlan CreatePlan(
            Document document,
            RoundedCornerRequest request,
            double radiusMm)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            return CreatePlan(
                document,
                request,
                radiusMm,
                document.Application.ShortCurveTolerance,
                true);
        }

        private static RoundedCornerPlan CreatePlan(
            Document document,
            RoundedCornerRequest request,
            double radiusMm,
            double shortCurveTolerance,
            bool validateExtension)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (radiusMm <= 0 || double.IsNaN(radiusMm) || double.IsInfinity(radiusMm))
                throw new InvalidOperationException("Informe um raio maior que zero.");

            var first = RoundedCornerMember.Get(document, request.FirstId, "primeiro");
            var second = RoundedCornerMember.Get(document, request.SecondId, "segundo");
            ValidatePair(first, second);

            var firstLine = first.GetAxis();
            var secondLine = second.GetAxis();
            var solution = Calculate(
                firstLine,
                secondLine,
                radiusMm / MillimetersPerFoot,
                shortCurveTolerance,
                validateExtension,
                GetVertexAnchor(first, second),
                request.FirstCornerEnd,
                request.SecondCornerEnd);

            first.EnsureCornerEndEditable(solution.FirstCornerEnd);
            second.EnsureCornerEndEditable(solution.SecondCornerEnd);
            RoundedCornerStore.EnsureEndpointsAreAvailable(
                document,
                first.Instance,
                solution.FirstCornerEnd,
                second.Instance,
                solution.SecondCornerEnd);

            return new RoundedCornerPlan
            {
                FirstId = request.FirstId,
                SecondId = request.SecondId,
                OriginalFirstLine = CloneBoundLine(firstLine),
                OriginalSecondLine = CloneBoundLine(secondLine),
                FirstCornerEnd = solution.FirstCornerEnd,
                SecondCornerEnd = solution.SecondCornerEnd,
                Vertex = solution.Vertex,
                FirstTangent = solution.FirstTangent,
                SecondTangent = solution.SecondTangent,
                PointOnArc = solution.PointOnArc,
                RadiusMm = radiusMm,
                RayAngleRadians = solution.RayAngleRadians
            };
        }

        internal static RoundedCornerCompoundPlan CreateCompoundPlan(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId middleId,
            int middleFirstCornerEnd,
            ElementId secondId,
            int secondCornerEnd,
            double radiusMm)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (firstId == null || middleId == null || secondId == null ||
                firstId.Equals(middleId) || firstId.Equals(secondId) ||
                middleId.Equals(secondId))
            {
                throw new InvalidOperationException(
                    "Selecione três corrimãos diferentes para a união pelo patamar.");
            }

            ValidateCornerEnd(firstCornerEnd, "primeiro corrimão");
            ValidateCornerEnd(middleFirstCornerEnd, "trecho horizontal");
            ValidateCornerEnd(secondCornerEnd, "segundo corrimão");

            var middle = RoundedCornerMember.Get(document, middleId, "intermediário");
            if (!middle.IsBeam)
                throw new InvalidOperationException(
                    "O trecho intermediário deve ser uma viga estrutural reta.");
            EnsureHorizontal(middle.GetAxis());

            var firstPlan = CreatePlan(
                document,
                new RoundedCornerRequest(
                    firstId,
                    middleId,
                    firstCornerEnd,
                    middleFirstCornerEnd),
                radiusMm);
            var secondPlan = CreatePlan(
                document,
                new RoundedCornerRequest(
                    middleId,
                    secondId,
                    1 - middleFirstCornerEnd,
                    secondCornerEnd),
                radiusMm);

            ValidateCompoundSpacing(
                document,
                firstPlan,
                secondPlan,
                middleFirstCornerEnd);

            return new RoundedCornerCompoundPlan
            {
                FirstCorner = firstPlan,
                SecondCorner = secondPlan,
                MiddleId = middleId
            };
        }

        internal static void ValidateCompoundSelection(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId middleId,
            int middleFirstCornerEnd,
            ElementId secondId,
            int secondCornerEnd)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (firstId == null || middleId == null || secondId == null ||
                firstId.Equals(middleId) || firstId.Equals(secondId) ||
                middleId.Equals(secondId))
            {
                throw new InvalidOperationException(
                    "Selecione três corrimãos diferentes para a união pelo patamar.");
            }

            ValidateCornerEnd(firstCornerEnd, "primeiro corrimão");
            ValidateCornerEnd(middleFirstCornerEnd, "trecho horizontal");
            ValidateCornerEnd(secondCornerEnd, "segundo corrimão");

            var middle = RoundedCornerMember.Get(document, middleId, "intermediário");
            if (!middle.IsBeam)
                throw new InvalidOperationException(
                    "O trecho intermediário deve ser uma viga estrutural reta.");
            EnsureHorizontal(middle.GetAxis());

            var firstPlan = CreatePlan(
                document,
                new RoundedCornerRequest(
                    firstId,
                    middleId,
                    firstCornerEnd,
                    middleFirstCornerEnd),
                0.1,
                0.0,
                false);
            var secondPlan = CreatePlan(
                document,
                new RoundedCornerRequest(
                    middleId,
                    secondId,
                    1 - middleFirstCornerEnd,
                    secondCornerEnd),
                0.1,
                0.0,
                false);
            ValidateCompoundSpacing(
                document,
                firstPlan,
                secondPlan,
                middleFirstCornerEnd);
        }

        internal static void ValidateAutomaticCompoundSelection(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId secondId,
            int secondCornerEnd,
            Line referenceAxis)
        {
            CreateAutomaticCompoundPlan(
                document,
                firstId,
                firstCornerEnd,
                secondId,
                secondCornerEnd,
                referenceAxis,
                0.1,
                0.0,
                false);
        }

        internal static void ValidateReferenceRouteSelection(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId secondId,
            int secondCornerEnd,
            Line referenceAxis)
        {
            CreateReferenceRoutePlan(
                document,
                firstId,
                firstCornerEnd,
                secondId,
                secondCornerEnd,
                referenceAxis,
                0.1,
                0.0,
                false);
        }

        internal static bool TryCreateMixedAutomaticReferenceAxis(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId secondId,
            int secondCornerEnd,
            out Line referenceAxis)
        {
            referenceAxis = null;
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            ValidateCornerEnd(firstCornerEnd, "primeiro corrimão");
            ValidateCornerEnd(secondCornerEnd, "segundo corrimão");

            var first = RoundedCornerMember.Get(document, firstId, "primeiro");
            var second = RoundedCornerMember.Get(document, secondId, "segundo");
            if (!first.IsBeam || !second.IsBeam)
                return false;

            var firstLine = first.GetAxis();
            var secondLine = second.GetAxis();
            bool firstHorizontal = IsHorizontalAxis(firstLine);
            bool secondHorizontal = IsHorizontalAxis(secondLine);
            if (firstHorizontal == secondHorizontal)
                return false;

            var horizontalLine = firstHorizontal ? firstLine : secondLine;
            var inclinedLine = firstHorizontal ? secondLine : firstLine;
            int horizontalEnd = firstHorizontal ? firstCornerEnd : secondCornerEnd;
            var selectedHorizontalEnd = horizontalLine.GetEndPoint(horizontalEnd);
            var inclinedDirection = inclinedLine.Direction;
            if (Math.Abs(inclinedDirection.Z) <= 1e-9)
                return false;

            var inclinedStart = inclinedLine.GetEndPoint(0);
            double inclinedParameter =
                (selectedHorizontalEnd.Z - inclinedStart.Z) /
                inclinedDirection.Z;
            var inclinedVertex = inclinedStart +
                                 inclinedDirection * inclinedParameter;
            if (!IsFinite(inclinedVertex))
                throw new InvalidOperationException(
                    "Não foi possível prolongar o corrimão inclinado até a altura " +
                    "do corrimão horizontal.");

            // O encontro no membro horizontal não depende da posição atual de sua
            // ponta. Usa a projeção ortogonal do ponto alcançado pelo inclinado sobre
            // o eixo horizontal infinito; o membro pode então ser recortado ou
            // prolongado sem girar, e o patamar não fica diagonal arbitrariamente.
            var horizontalStart = horizontalLine.GetEndPoint(0);
            var horizontalDirection = horizontalLine.Direction;
            var horizontalVertex = horizontalStart + horizontalDirection *
                (inclinedVertex - horizontalStart)
                    .DotProduct(horizontalDirection);

            double minimumLength = Math.Max(
                document.Application.ShortCurveTolerance,
                1.0 / MillimetersPerFoot);
            if (inclinedVertex.DistanceTo(horizontalVertex) <= minimumLength)
                throw new InvalidOperationException(
                    "Os eixos já se encontram na altura do corrimão horizontal. " +
                    "Use a união direta.");

            referenceAxis = firstHorizontal
                ? Line.CreateBound(horizontalVertex, inclinedVertex)
                : Line.CreateBound(inclinedVertex, horizontalVertex);
            EnsureHorizontal(referenceAxis);
            return true;
        }

        internal static bool TryCreateInclinedPairReferenceAxis(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId secondId,
            int secondCornerEnd,
            Line elevationReference,
            out Line referenceAxis)
        {
            referenceAxis = null;
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            ValidateCornerEnd(firstCornerEnd, "primeiro corrimão");
            ValidateCornerEnd(secondCornerEnd, "segundo corrimão");
            ValidateHorizontalReference(elevationReference);

            var first = RoundedCornerMember.Get(document, firstId, "primeiro");
            var second = RoundedCornerMember.Get(document, secondId, "segundo");
            if (!first.IsBeam || !second.IsBeam)
                return false;

            var firstLine = first.GetAxis();
            var secondLine = second.GetAxis();
            if (IsHorizontalAxis(firstLine) || IsHorizontalAxis(secondLine))
                return false;

            double targetElevation = elevationReference.GetEndPoint(0).Z;
            XYZ PointAtElevation(Line line, string label)
            {
                var direction = line.Direction;
                if (Math.Abs(direction.Z) <= 1e-9)
                    throw new InvalidOperationException(
                        $"O eixo do {label} não alcança a altura informada.");
                var start = line.GetEndPoint(0);
                double parameter = (targetElevation - start.Z) / direction.Z;
                var point = start + direction * parameter;
                if (!IsFinite(point))
                    throw new InvalidOperationException(
                        $"Não foi possível calcular o encontro do {label} na altura informada.");
                return point;
            }

            var firstVertex = PointAtElevation(firstLine, "primeiro corrimão");
            var secondVertex = PointAtElevation(secondLine, "segundo corrimão");
            double minimumLength = Math.Max(
                document.Application.ShortCurveTolerance,
                1.0 / MillimetersPerFoot);
            if (firstVertex.DistanceTo(secondVertex) <= minimumLength)
                throw new InvalidOperationException(
                    "O trecho horizontal automático ficaria curto demais nessa altura.");

            referenceAxis = Line.CreateBound(firstVertex, secondVertex);
            EnsureHorizontal(referenceAxis);
            return true;
        }

        internal static RoundedCornerAutomaticCompoundPlan CreateAutomaticCompoundPlan(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId secondId,
            int secondCornerEnd,
            Line referenceAxis,
            double radiusMm)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            return CreateAutomaticCompoundPlan(
                document,
                firstId,
                firstCornerEnd,
                secondId,
                secondCornerEnd,
                referenceAxis,
                radiusMm,
                document.Application.ShortCurveTolerance,
                true);
        }

        private static RoundedCornerAutomaticCompoundPlan CreateAutomaticCompoundPlan(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId secondId,
            int secondCornerEnd,
            Line referenceAxis,
            double radiusMm,
            double shortCurveTolerance,
            bool validateExtension)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (radiusMm <= 0 || double.IsNaN(radiusMm) || double.IsInfinity(radiusMm))
                throw new InvalidOperationException("Informe um raio maior que zero.");
            ValidateHorizontalReference(referenceAxis);
            ValidateCornerEnd(firstCornerEnd, "primeiro corrimão");
            ValidateCornerEnd(secondCornerEnd, "segundo corrimão");

            var first = RoundedCornerMember.Get(document, firstId, "primeiro");
            var second = RoundedCornerMember.Get(document, secondId, "segundo");
            if (!first.IsBeam || !second.IsBeam)
                throw new InvalidOperationException(
                    "O patamar automático aceita somente vigas estruturais retas.");
            ValidatePair(first, second);

            var firstLine = first.GetAxis();
            var secondLine = second.GetAxis();
            var middleAxis = CalculateAutomaticMiddleAxis(
                document,
                firstLine,
                firstCornerEnd,
                secondLine,
                secondCornerEnd,
                referenceAxis);
            ValidateForcedEndpointAdjustment(
                firstLine.GetEndPoint(firstCornerEnd),
                middleAxis.GetEndPoint(0),
                "primeiro corrimão");
            ValidateForcedEndpointAdjustment(
                secondLine.GetEndPoint(secondCornerEnd),
                middleAxis.GetEndPoint(1),
                "segundo corrimão");
            var adjustedFirstLine = ReplaceEndpoint(
                firstLine,
                firstCornerEnd,
                middleAxis.GetEndPoint(0));
            var adjustedSecondLine = ReplaceEndpoint(
                secondLine,
                secondCornerEnd,
                middleAxis.GetEndPoint(1));

            var firstSolution = Calculate(
                adjustedFirstLine,
                middleAxis,
                radiusMm / MillimetersPerFoot,
                shortCurveTolerance,
                validateExtension,
                -1,
                firstCornerEnd,
                0);
            var secondSolution = Calculate(
                middleAxis,
                adjustedSecondLine,
                radiusMm / MillimetersPerFoot,
                shortCurveTolerance,
                validateExtension,
                -1,
                1,
                secondCornerEnd);

            first.EnsureCornerEndEditable(firstSolution.FirstCornerEnd);
            second.EnsureCornerEndEditable(secondSolution.SecondCornerEnd);
            RoundedCornerStore.EnsureEndpointsAreAvailable(
                document,
                first.Instance,
                firstSolution.FirstCornerEnd,
                second.Instance,
                secondSolution.SecondCornerEnd);
            ValidateCompoundSpacing(
                document,
                middleAxis,
                firstSolution.SecondTangent,
                secondSolution.FirstTangent,
                0);

            return new RoundedCornerAutomaticCompoundPlan
            {
                FirstId = firstId,
                SecondId = secondId,
                FirstCornerEnd = firstCornerEnd,
                SecondCornerEnd = secondCornerEnd,
                MiddleAxis = middleAxis,
                RadiusMm = radiusMm
            };
        }

        internal static RoundedCornerReferenceRoutePlan CreateReferenceRoutePlan(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId secondId,
            int secondCornerEnd,
            Line referenceAxis,
            double radiusMm)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            return CreateReferenceRoutePlan(
                document,
                firstId,
                firstCornerEnd,
                secondId,
                secondCornerEnd,
                referenceAxis,
                radiusMm,
                document.Application.ShortCurveTolerance,
                true);
        }

        private static RoundedCornerReferenceRoutePlan CreateReferenceRoutePlan(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId secondId,
            int secondCornerEnd,
            Line referenceAxis,
            double radiusMm,
            double shortCurveTolerance,
            bool validateExtension)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (radiusMm <= 0 || double.IsNaN(radiusMm) || double.IsInfinity(radiusMm))
                throw new InvalidOperationException("Informe um raio maior que zero.");
            ValidateHorizontalReference(referenceAxis);
            ValidateCornerEnd(firstCornerEnd, "primeiro corrimão");
            ValidateCornerEnd(secondCornerEnd, "segundo corrimão");

            var first = RoundedCornerMember.Get(document, firstId, "primeiro");
            var second = RoundedCornerMember.Get(document, secondId, "segundo");
            if (!first.IsBeam || !second.IsBeam)
                throw new InvalidOperationException(
                    "A rota pela aresta aceita somente vigas estruturais retas.");
            ValidatePair(first, second);

            var firstLine = first.GetAxis();
            var secondLine = second.GetAxis();
            bool firstHorizontal = IsHorizontalAxis(firstLine);
            bool secondHorizontal = IsHorizontalAxis(secondLine);
            if (firstHorizontal == secondHorizontal)
            {
                throw new InvalidOperationException(
                    "A rota pela aresta exige exatamente um corrimão inclinado e um horizontal.");
            }

            var inclined = firstHorizontal ? second : first;
            var horizontal = firstHorizontal ? first : second;
            var inclinedLine = firstHorizontal ? secondLine : firstLine;
            var horizontalLine = firstHorizontal ? firstLine : secondLine;
            int inclinedCornerEnd = firstHorizontal ? secondCornerEnd : firstCornerEnd;
            int horizontalCornerEnd = firstHorizontal ? firstCornerEnd : secondCornerEnd;

            var referenceStart = referenceAxis.GetEndPoint(0);
            var referenceEnd = referenceAxis.GetEndPoint(1);
            double elevationToleranceFt = Math.Max(
                AxisIntersectionToleranceMm / MillimetersPerFoot,
                document.Application.VertexTolerance);
            if (Math.Abs(referenceEnd.Z - referenceStart.Z) > elevationToleranceFt)
            {
                throw new InvalidOperationException(
                    "A aresta de referência precisa definir uma única cota horizontal.");
            }

            // Em um par misto, a cota funcional da união vem sempre do corrimão
            // horizontal. A aresta selecionada controla somente posição em planta,
            // direção e afastamento lateral.
            double referenceElevation =
                (horizontalLine.GetEndPoint(0).Z +
                 horizontalLine.GetEndPoint(1).Z) * 0.5;
            var normalizedReferenceAxis = Line.CreateBound(
                new XYZ(referenceStart.X, referenceStart.Y, referenceElevation),
                new XYZ(referenceEnd.X, referenceEnd.Y, referenceElevation));

            var inclinedSelectedPoint =
                inclinedLine.GetEndPoint(inclinedCornerEnd);
            var inclinedInteriorDirection = Direction(
                inclinedSelectedPoint,
                inclinedLine.GetEndPoint(1 - inclinedCornerEnd),
                "inclinado");
            var levelDirection = new XYZ(
                -inclinedInteriorDirection.X,
                -inclinedInteriorDirection.Y,
                0.0);
            if (!IsFinite(levelDirection) || levelDirection.GetLength() <= 1e-9)
            {
                throw new InvalidOperationException(
                    "O corrimão inclinado não possui uma direção válida em planta.");
            }
            levelDirection = levelDirection.Normalize();

            var inclinedVertex = PointOnAxisAtElevation(
                inclinedLine,
                referenceElevation,
                "corrimão inclinado");
            ResolveCornerEnd(
                inclinedLine,
                inclinedVertex,
                inclinedCornerEnd,
                "inclinado");

            var referenceDirection = HorizontalDirection(
                referenceAxis,
                "aresta de referência");
            var referenceOrigin = new XYZ(
                referenceStart.X,
                referenceStart.Y,
                referenceElevation);
            var referenceVertex = IntersectHorizontalSupportingLines(
                inclinedVertex,
                levelDirection,
                referenceOrigin,
                referenceDirection,
                referenceElevation,
                "O trecho nivelado é paralelo à aresta de referência.");

            double minimumLengthFt = Math.Max(
                document.Application.ShortCurveTolerance,
                1.0 / MillimetersPerFoot);
            double levelStation =
                (referenceVertex - inclinedVertex).DotProduct(levelDirection);
            SagaLog.Write(
                $"Rota por aresta - direção: cornerEnd={inclinedCornerEnd}, " +
                $"estação={levelStation * MillimetersPerFoot:F1}mm, " +
                $"selecionado=({inclinedSelectedPoint.X * MillimetersPerFoot:F1}," +
                $"{inclinedSelectedPoint.Y * MillimetersPerFoot:F1}," +
                $"{inclinedSelectedPoint.Z * MillimetersPerFoot:F1}), " +
                $"vérticeInclinado=({inclinedVertex.X * MillimetersPerFoot:F1}," +
                $"{inclinedVertex.Y * MillimetersPerFoot:F1}," +
                $"{inclinedVertex.Z * MillimetersPerFoot:F1}), " +
                $"vérticeReferência=({referenceVertex.X * MillimetersPerFoot:F1}," +
                $"{referenceVertex.Y * MillimetersPerFoot:F1}," +
                $"{referenceVertex.Z * MillimetersPerFoot:F1}), " +
                $"direçãoNivelada=({levelDirection.X:F6}," +
                $"{levelDirection.Y:F6},0).");
            if (levelStation <= minimumLengthFt)
            {
                throw new InvalidOperationException(
                    "A aresta de referência fica atrás da continuação nivelada do " +
                    "corrimão inclinado. Selecione a aresta do outro lado da união.");
            }

            var horizontalDirection = HorizontalDirection(
                horizontalLine,
                "corrimão horizontal");
            var horizontalOrigin = new XYZ(
                horizontalLine.GetEndPoint(0).X,
                horizontalLine.GetEndPoint(0).Y,
                referenceElevation);
            var horizontalVertex = IntersectHorizontalSupportingLines(
                referenceOrigin,
                referenceDirection,
                horizontalOrigin,
                horizontalDirection,
                referenceElevation,
                "A aresta de referência é paralela ao corrimão horizontal.");
            ResolveCornerEnd(
                horizontalLine,
                horizontalVertex,
                horizontalCornerEnd,
                "horizontal");

            if (inclinedVertex.DistanceTo(referenceVertex) <= minimumLengthFt)
            {
                throw new InvalidOperationException(
                    "O trecho nivelado entre o inclinado e a referência ficaria curto demais.");
            }
            if (referenceVertex.DistanceTo(horizontalVertex) <= minimumLengthFt)
            {
                throw new InvalidOperationException(
                    "O trecho paralelo à aresta de referência ficaria curto demais.");
            }

            var levelAxis = Line.CreateBound(inclinedVertex, referenceVertex);
            var connectorAxis = Line.CreateBound(referenceVertex, horizontalVertex);
            EnsureHorizontal(levelAxis);
            EnsureHorizontal(connectorAxis);

            var firstSolution = Calculate(
                inclinedLine,
                levelAxis,
                radiusMm / MillimetersPerFoot,
                shortCurveTolerance,
                validateExtension,
                -1,
                inclinedCornerEnd,
                0);
            var secondSolution = Calculate(
                levelAxis,
                connectorAxis,
                radiusMm / MillimetersPerFoot,
                shortCurveTolerance,
                validateExtension,
                -1,
                1,
                0);
            var thirdSolution = Calculate(
                connectorAxis,
                horizontalLine,
                radiusMm / MillimetersPerFoot,
                shortCurveTolerance,
                validateExtension,
                -1,
                1,
                horizontalCornerEnd);

            ValidateCompoundSpacing(
                document,
                levelAxis,
                firstSolution.SecondTangent,
                secondSolution.FirstTangent,
                0);
            ValidateCompoundSpacing(
                document,
                connectorAxis,
                secondSolution.SecondTangent,
                thirdSolution.FirstTangent,
                0);

            inclined.EnsureCornerEndEditable(inclinedCornerEnd);
            horizontal.EnsureCornerEndEditable(horizontalCornerEnd);
            RoundedCornerStore.EnsureEndpointsAreAvailable(
                document,
                inclined.Instance,
                inclinedCornerEnd,
                horizontal.Instance,
                horizontalCornerEnd);

            SagaLog.Write(
                $"Rota por aresta: inclinado={inclined.Instance.Id.GetId()}, " +
                $"horizontal={horizontal.Instance.Id.GetId()}, " +
                $"nivelado={levelAxis.Length * MillimetersPerFoot:F1}mm, " +
                $"referência={connectorAxis.Length * MillimetersPerFoot:F1}mm, " +
                $"cota={referenceElevation * MillimetersPerFoot:F1}mm.");

            return new RoundedCornerReferenceRoutePlan
            {
                InclinedId = inclined.Instance.Id,
                HorizontalId = horizontal.Instance.Id,
                InclinedCornerEnd = inclinedCornerEnd,
                HorizontalCornerEnd = horizontalCornerEnd,
                ReferenceAxis = normalizedReferenceAxis,
                LevelAxis = levelAxis,
                ConnectorAxis = connectorAxis,
                RadiusMm = radiusMm
            };
        }

        internal static RoundedCornerResult Apply(
            Document document,
            ElementId firstId,
            ElementId secondId,
            double radiusMm)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            RoundedCornerStore.RemoveStaleEntries(document);
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
            document.Regenerate();
            EnsureArcWasApplied(curved, arc);

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

        internal static RoundedCornerResult Apply(
            Document document,
            RoundedCornerPlan plan,
            string operationId = null,
            bool forceRegistration = false,
            string ownedIntermediateElementUniqueId = null)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            RoundedCornerStore.RemoveStaleEntries(document);

            var first = RoundedCornerMember.Get(document, plan.FirstId, "primeiro");
            var second = RoundedCornerMember.Get(document, plan.SecondId, "segundo");
            ValidatePair(first, second);

            first.EnsureCornerEndEditable(plan.FirstCornerEnd);
            second.EnsureCornerEndEditable(plan.SecondCornerEnd);
            RoundedCornerStore.EnsureEndpointsAreAvailable(
                document,
                first.Instance,
                plan.FirstCornerEnd,
                second.Instance,
                plan.SecondCornerEnd);

            bool firstJoinWasAllowed = first.IsJoinAllowedAtEnd(plan.FirstCornerEnd);
            bool secondJoinWasAllowed = second.IsJoinAllowedAtEnd(plan.SecondCornerEnd);
            first.DisallowJoinAtEnd(plan.FirstCornerEnd);
            second.DisallowJoinAtEnd(plan.SecondCornerEnd);

            first.SetCornerEndpoint(plan.FirstCornerEnd, plan.FirstTangent);
            second.SetCornerEndpoint(plan.SecondCornerEnd, plan.SecondTangent);
            document.Regenerate();
            EnsureEndpointWasApplied(first, plan.FirstCornerEnd, plan.FirstTangent);
            EnsureEndpointWasApplied(second, plan.SecondCornerEnd, plan.SecondTangent);

            var beam = first.IsBeam ? first : second;
            var symbol = beam.Instance.Symbol;
            if (!symbol.IsActive) symbol.Activate();

            var level = GetReferenceLevel(document, beam.Instance, plan.Vertex.Z);
            var arc = Arc.Create(
                plan.FirstTangent,
                plan.SecondTangent,
                plan.PointOnArc);
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

            if (curved.Location is LocationCurve curvedLocation)
                curvedLocation.Curve = arc;
            document.Regenerate();
            EnsureArcWasApplied(curved, arc);

            RoundedCornerStore.RegisterIfNeeded(
                document,
                first,
                plan.OriginalFirstLine,
                plan.FirstCornerEnd,
                firstJoinWasAllowed,
                second,
                plan.OriginalSecondLine,
                plan.SecondCornerEnd,
                secondJoinWasAllowed,
                curved,
                plan.RadiusMm,
                operationId,
                forceRegistration,
                ownedIntermediateElementUniqueId);

            return new RoundedCornerResult
            {
                CurvedElementId = curved.Id,
                Vertex = plan.Vertex,
                FirstTangent = plan.FirstTangent,
                SecondTangent = plan.SecondTangent,
                RadiusMm = plan.RadiusMm,
                TurnAngleDegrees =
                    (Math.PI - plan.RayAngleRadians) * 180.0 / Math.PI
            };
        }

        internal static RoundedCornerResult[] ApplyCompound(
            Document document,
            RoundedCornerCompoundPlan plan,
            string ownedIntermediateElementUniqueId = null)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            string operationId = Guid.NewGuid().ToString("N");
            bool registerWholeOperation =
                HasRegisteredAssembly(document, plan.FirstCorner.FirstId) ||
                HasRegisteredAssembly(document, plan.MiddleId) ||
                HasRegisteredAssembly(document, plan.SecondCorner.SecondId);
            var firstResult = Apply(
                document,
                plan.FirstCorner,
                operationId,
                registerWholeOperation,
                ownedIntermediateElementUniqueId);
            var secondResult = Apply(
                document,
                plan.SecondCorner,
                operationId,
                registerWholeOperation,
                ownedIntermediateElementUniqueId);
            EnsureCompoundTangency(
                document,
                plan,
                firstResult,
                secondResult);
            return new[] { firstResult, secondResult };
        }

        /// <summary>
        /// Cria o trecho horizontal calculado e os dois cantos em uma única
        /// transação já aberta pelo chamador. Qualquer falha deve provocar o
        /// rollback da operação inteira.
        /// </summary>
        internal static RoundedCornerAutomaticCompoundResult ApplyAutomaticCompound(
            Document document,
            RoundedCornerAutomaticCompoundPlan plan)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            // Recalcula sobre os eixos atuais para não aplicar uma proposta que
            // tenha ficado obsoleta enquanto a janela do raio estava aberta.
            var currentPlan = CreateAutomaticCompoundPlan(
                document,
                plan.FirstId,
                plan.FirstCornerEnd,
                plan.SecondId,
                plan.SecondCornerEnd,
                plan.MiddleAxis,
                plan.RadiusMm);

            var first = RoundedCornerMember.Get(
                document,
                currentPlan.FirstId,
                "primeiro");
            var second = RoundedCornerMember.Get(
                document,
                currentPlan.SecondId,
                "segundo");
            var originalFirstAxis = CloneBoundLine(first.GetAxis());
            var originalSecondAxis = CloneBoundLine(second.GetAxis());
            var symbol = first.Instance.Symbol;
            if (symbol == null)
                throw new InvalidOperationException(
                    "O primeiro corrimão não possui um tipo válido para criar o patamar.");
            if (!symbol.IsActive) symbol.Activate();

            var level = GetReferenceLevel(
                document,
                first.Instance,
                currentPlan.MiddleAxis.GetEndPoint(0).Z);
            var middle = document.Create.NewFamilyInstance(
                currentPlan.MiddleAxis,
                symbol,
                level,
                StructuralType.Beam);
            if (middle == null)
                throw new InvalidOperationException(
                    "A família selecionada não aceitou a criação do trecho horizontal.");

            CopyPlacementParameters(first.Instance, middle);
            document.Regenerate();
            StructuralFramingUtils.DisallowJoinAtEnd(middle, 0);
            StructuralFramingUtils.DisallowJoinAtEnd(middle, 1);

            if (!(middle.Location is LocationCurve middleLocation))
                throw new InvalidOperationException(
                    "O trecho horizontal criado não possui um eixo editável.");
            middleLocation.Curve = currentPlan.MiddleAxis;
            document.Regenerate();
            EnsureLineWasApplied(middle, currentPlan.MiddleAxis);

            var actualMiddle = RoundedCornerMember.Get(
                document,
                middle.Id,
                "intermediário automático");
            var actualMiddleAxis = actualMiddle.GetAxis();
            int middleFirstCornerEnd = NearestEnd(
                actualMiddleAxis,
                currentPlan.MiddleAxis.GetEndPoint(0));

            var firstTarget = actualMiddleAxis.GetEndPoint(middleFirstCornerEnd);
            var secondTarget = actualMiddleAxis.GetEndPoint(1 - middleFirstCornerEnd);
            ValidateForcedEndpointAdjustment(
                originalFirstAxis.GetEndPoint(currentPlan.FirstCornerEnd),
                firstTarget,
                "primeiro corrimão");
            ValidateForcedEndpointAdjustment(
                originalSecondAxis.GetEndPoint(currentPlan.SecondCornerEnd),
                secondTarget,
                "segundo corrimão");

            first.DisallowJoinAtEnd(currentPlan.FirstCornerEnd);
            second.DisallowJoinAtEnd(currentPlan.SecondCornerEnd);
            first.SetCornerEndpoint(currentPlan.FirstCornerEnd, firstTarget);
            second.SetCornerEndpoint(currentPlan.SecondCornerEnd, secondTarget);
            document.Regenerate();

            var compoundPlan = CreateCompoundPlan(
                document,
                currentPlan.FirstId,
                currentPlan.FirstCornerEnd,
                middle.Id,
                middleFirstCornerEnd,
                currentPlan.SecondId,
                currentPlan.SecondCornerEnd,
                currentPlan.RadiusMm);
            compoundPlan.FirstCorner.OriginalFirstLine = CloneBoundLine(originalFirstAxis);
            compoundPlan.SecondCorner.OriginalSecondLine = CloneBoundLine(originalSecondAxis);
            var results = ApplyCompound(
                document,
                compoundPlan,
                middle.UniqueId);
            EnsureOriginalAxisWasPreserved(
                first,
                originalFirstAxis,
                "primeiro corrimão");
            EnsureOriginalAxisWasPreserved(
                second,
                originalSecondAxis,
                "segundo corrimão");

            return new RoundedCornerAutomaticCompoundResult
            {
                MiddleElementId = middle.Id,
                CornerResults = results
            };
        }

        internal static RoundedCornerReferenceRouteResult ApplyReferenceRoute(
            Document document,
            RoundedCornerReferenceRoutePlan plan)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            // Recalcula a rota sobre os eixos atuais. A referência mantém sua posição
            // e direção em planta; sua cota já foi normalizada pelo corrimão horizontal.
            var currentPlan = CreateReferenceRoutePlan(
                document,
                plan.InclinedId,
                plan.InclinedCornerEnd,
                plan.HorizontalId,
                plan.HorizontalCornerEnd,
                plan.ReferenceAxis,
                plan.RadiusMm);

            var inclined = RoundedCornerMember.Get(
                document,
                currentPlan.InclinedId,
                "inclinado");
            var horizontal = RoundedCornerMember.Get(
                document,
                currentPlan.HorizontalId,
                "horizontal");
            var originalInclinedAxis = CloneBoundLine(inclined.GetAxis());
            var originalHorizontalAxis = CloneBoundLine(horizontal.GetAxis());
            var symbol = inclined.Instance.Symbol;
            if (symbol == null)
                throw new InvalidOperationException(
                    "O corrimão inclinado não possui um tipo válido para criar a rota.");
            if (!symbol.IsActive) symbol.Activate();

            var level = GetReferenceLevel(
                document,
                inclined.Instance,
                currentPlan.LevelAxis.GetEndPoint(0).Z);
            var levelMember = CreateReferenceRouteMember(
                document,
                inclined.Instance,
                symbol,
                level,
                currentPlan.LevelAxis,
                "trecho nivelado");
            var connectorMember = CreateReferenceRouteMember(
                document,
                inclined.Instance,
                symbol,
                level,
                currentPlan.ConnectorAxis,
                "trecho paralelo à referência");

            // A criação do segundo perfil pode provocar um ajuste automático de join.
            // Reaplica e verifica os dois eixos antes de calcular as tangências.
            SetMemberAxis(levelMember, currentPlan.LevelAxis);
            SetMemberAxis(connectorMember, currentPlan.ConnectorAxis);
            document.Regenerate();
            EnsureLineWasApplied(levelMember, currentPlan.LevelAxis);
            EnsureLineWasApplied(connectorMember, currentPlan.ConnectorAxis);

            var actualLevel = RoundedCornerMember.Get(
                document,
                levelMember.Id,
                "nivelado");
            var actualConnector = RoundedCornerMember.Get(
                document,
                connectorMember.Id,
                "paralelo à referência");
            var actualLevelAxis = actualLevel.GetAxis();
            var actualConnectorAxis = actualConnector.GetAxis();
            int levelInclinedEnd = NearestEnd(
                actualLevelAxis,
                currentPlan.LevelAxis.GetEndPoint(0));
            int levelReferenceEnd = 1 - levelInclinedEnd;
            int connectorReferenceEnd = NearestEnd(
                actualConnectorAxis,
                currentPlan.ConnectorAxis.GetEndPoint(0));
            int connectorHorizontalEnd = 1 - connectorReferenceEnd;

            var firstCorner = CreatePlan(
                document,
                new RoundedCornerRequest(
                    currentPlan.InclinedId,
                    levelMember.Id,
                    currentPlan.InclinedCornerEnd,
                    levelInclinedEnd),
                currentPlan.RadiusMm);
            var secondCorner = CreatePlan(
                document,
                new RoundedCornerRequest(
                    levelMember.Id,
                    connectorMember.Id,
                    levelReferenceEnd,
                    connectorReferenceEnd),
                currentPlan.RadiusMm);
            var thirdCorner = CreatePlan(
                document,
                new RoundedCornerRequest(
                    connectorMember.Id,
                    currentPlan.HorizontalId,
                    connectorHorizontalEnd,
                    currentPlan.HorizontalCornerEnd),
                currentPlan.RadiusMm);

            ValidateCompoundSpacing(
                document,
                firstCorner,
                secondCorner,
                levelInclinedEnd);
            ValidateCompoundSpacing(
                document,
                secondCorner,
                thirdCorner,
                connectorReferenceEnd);

            string operationId = Guid.NewGuid().ToString("N");
            bool registerWholeOperation =
                HasRegisteredAssembly(document, currentPlan.InclinedId) ||
                HasRegisteredAssembly(document, currentPlan.HorizontalId);
            var firstResult = Apply(
                document,
                firstCorner,
                operationId,
                registerWholeOperation,
                levelMember.UniqueId);
            var secondResult = Apply(
                document,
                secondCorner,
                operationId,
                registerWholeOperation,
                connectorMember.UniqueId);
            var thirdResult = Apply(
                document,
                thirdCorner,
                operationId,
                registerWholeOperation,
                connectorMember.UniqueId);

            EnsureReferenceRouteTangency(
                document,
                levelMember.Id,
                connectorMember.Id,
                firstCorner,
                secondCorner,
                thirdCorner,
                firstResult,
                secondResult,
                thirdResult);
            EnsureOriginalAxisWasPreserved(
                inclined,
                originalInclinedAxis,
                "corrimão inclinado");
            EnsureOriginalAxisWasPreserved(
                horizontal,
                originalHorizontalAxis,
                "corrimão horizontal");

            return new RoundedCornerReferenceRouteResult
            {
                LevelElementId = levelMember.Id,
                ConnectorElementId = connectorMember.Id,
                CornerResults = new[]
                {
                    firstResult,
                    secondResult,
                    thirdResult
                }
            };
        }

        private static FamilyInstance CreateReferenceRouteMember(
            Document document,
            FamilyInstance source,
            FamilySymbol symbol,
            Level level,
            Line axis,
            string label)
        {
            var instance = document.Create.NewFamilyInstance(
                axis,
                symbol,
                level,
                StructuralType.Beam);
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"A família selecionada não aceitou a criação do {label}.");
            }

            CopyPlacementParameters(source, instance);
            document.Regenerate();
            StructuralFramingUtils.DisallowJoinAtEnd(instance, 0);
            StructuralFramingUtils.DisallowJoinAtEnd(instance, 1);
            SetMemberAxis(instance, axis);
            document.Regenerate();
            EnsureLineWasApplied(instance, axis);
            return instance;
        }

        private static void SetMemberAxis(FamilyInstance instance, Line axis)
        {
            if (!(instance?.Location is LocationCurve location))
                throw new InvalidOperationException(
                    "Um dos trechos da rota não possui um eixo editável.");
            location.Curve = axis;
        }

        private static Line ReplaceEndpoint(Line line, int endpoint, XYZ point)
        {
            ValidateCornerEnd(endpoint, "perfil");
            return endpoint == 0
                ? Line.CreateBound(point, line.GetEndPoint(1))
                : Line.CreateBound(line.GetEndPoint(0), point);
        }

        private static void ValidateForcedEndpointAdjustment(
            XYZ originalPoint,
            XYZ targetPoint,
            string label)
        {
            double adjustmentMm = originalPoint.DistanceTo(targetPoint) *
                                  MillimetersPerFoot;
            if (adjustmentMm > MaximumAutomaticEndpointAdjustmentMm)
            {
                throw new InvalidOperationException(
                    $"O {label} precisaria ter sua ponta deslocada em " +
                    $"{adjustmentMm:F0} mm para alcançar o eixo definido. " +
                    $"O limite é {MaximumAutomaticEndpointAdjustmentMm:F0} mm.");
            }
        }

        private static bool HasRegisteredAssembly(
            Document document,
            ElementId elementId)
        {
            return document != null &&
                   elementId != null &&
                   RoundedCornerStore.TryGetRegisteredAssemblyId(
                       document.GetElement(elementId),
                       out _);
        }

        private static void EnsureCompoundTangency(
            Document document,
            RoundedCornerCompoundPlan plan,
            RoundedCornerResult firstResult,
            RoundedCornerResult secondResult)
        {
            var middle = RoundedCornerMember.Get(
                document,
                plan.MiddleId,
                "intermediário");
            var middleLine = middle.GetAxis();
            var middleDirection = Direction(
                middleLine.GetEndPoint(0),
                middleLine.GetEndPoint(1),
                "intermediário");

            EnsureArcTangentToDirection(
                document,
                firstResult.CurvedElementId,
                plan.FirstCorner.SecondTangent,
                middleDirection);
            EnsureArcTangentToDirection(
                document,
                secondResult.CurvedElementId,
                plan.SecondCorner.FirstTangent,
                middleDirection);
        }

        private static void EnsureReferenceRouteTangency(
            Document document,
            ElementId levelId,
            ElementId connectorId,
            RoundedCornerPlan firstCorner,
            RoundedCornerPlan secondCorner,
            RoundedCornerPlan thirdCorner,
            RoundedCornerResult firstResult,
            RoundedCornerResult secondResult,
            RoundedCornerResult thirdResult)
        {
            var level = RoundedCornerMember.Get(
                document,
                levelId,
                "nivelado");
            var connector = RoundedCornerMember.Get(
                document,
                connectorId,
                "paralelo à referência");
            var levelAxis = level.GetAxis();
            var connectorAxis = connector.GetAxis();
            var levelDirection = Direction(
                levelAxis.GetEndPoint(0),
                levelAxis.GetEndPoint(1),
                "nivelado");
            var connectorDirection = Direction(
                connectorAxis.GetEndPoint(0),
                connectorAxis.GetEndPoint(1),
                "paralelo à referência");

            EnsureArcTangentToDirection(
                document,
                firstResult.CurvedElementId,
                firstCorner.SecondTangent,
                levelDirection);
            EnsureArcTangentToDirection(
                document,
                secondResult.CurvedElementId,
                secondCorner.FirstTangent,
                levelDirection);
            EnsureArcTangentToDirection(
                document,
                secondResult.CurvedElementId,
                secondCorner.SecondTangent,
                connectorDirection);
            EnsureArcTangentToDirection(
                document,
                thirdResult.CurvedElementId,
                thirdCorner.FirstTangent,
                connectorDirection);
        }

        private static void EnsureArcTangentToDirection(
            Document document,
            ElementId curvedElementId,
            XYZ connectionPoint,
            XYZ expectedDirection)
        {
            var curved = document.GetElement(curvedElementId) as FamilyInstance;
            var arc = (curved?.Location as LocationCurve)?.Curve as Arc;
            if (arc == null)
                throw new InvalidOperationException(
                    "Não foi possível verificar a tangência de um dos arcos.");

            int endpoint = arc.GetEndPoint(0).DistanceTo(connectionPoint) <=
                           arc.GetEndPoint(1).DistanceTo(connectionPoint)
                ? 0
                : 1;
            var tangent = arc.ComputeDerivatives(endpoint, true).BasisX.Normalize();
            double dot = Math.Abs(tangent.DotProduct(expectedDirection));
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            double angleDegrees = Math.Acos(dot) * 180.0 / Math.PI;
            if (angleDegrees > 0.1)
            {
                throw new InvalidOperationException(
                    $"Os eixos do patamar não produziram uma tangência contínua " +
                    $"({angleDegrees:F2} graus de desvio). Alinhe melhor os perfis e tente novamente.");
            }
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

        private static void EnsureArcWasApplied(
            FamilyInstance curved,
            Arc expectedArc)
        {
            var actualArc = (curved?.Location as LocationCurve)?.Curve as Arc;
            if (actualArc == null)
                throw new InvalidOperationException(
                    "O Revit não manteve o conector como um arco estrutural.");

            double toleranceFt = Math.Max(
                0.5 / MillimetersPerFoot,
                curved.Document.Application.VertexTolerance);
            var expectedStart = expectedArc.GetEndPoint(0);
            var expectedEnd = expectedArc.GetEndPoint(1);
            var actualStart = actualArc.GetEndPoint(0);
            var actualEnd = actualArc.GetEndPoint(1);
            bool sameDirection =
                actualStart.DistanceTo(expectedStart) <= toleranceFt &&
                actualEnd.DistanceTo(expectedEnd) <= toleranceFt;
            bool reverseDirection =
                actualStart.DistanceTo(expectedEnd) <= toleranceFt &&
                actualEnd.DistanceTo(expectedStart) <= toleranceFt;
            double radiusDifference = Math.Abs(actualArc.Radius - expectedArc.Radius);
            double middleDifference = actualArc.Evaluate(0.5, true)
                .DistanceTo(expectedArc.Evaluate(0.5, true));
            if ((!sameDirection && !reverseDirection) ||
                radiusDifference > toleranceFt ||
                middleDifference > toleranceFt)
            {
                throw new InvalidOperationException(
                    "O Revit alterou a geometria do arco após a criação. " +
                    "A operação foi cancelada para não deixar uma união desalinhada.");
            }
        }

        private static void EnsureLineWasApplied(
            FamilyInstance instance,
            Line expectedLine)
        {
            var actualLine = (instance?.Location as LocationCurve)?.Curve as Line;
            if (actualLine == null)
                throw new InvalidOperationException(
                    "O Revit não manteve o patamar como uma viga estrutural reta.");

            double toleranceFt = Math.Max(
                0.5 / MillimetersPerFoot,
                instance.Document.Application.VertexTolerance);
            var expectedStart = expectedLine.GetEndPoint(0);
            var expectedEnd = expectedLine.GetEndPoint(1);
            var actualStart = actualLine.GetEndPoint(0);
            var actualEnd = actualLine.GetEndPoint(1);
            bool sameDirection =
                actualStart.DistanceTo(expectedStart) <= toleranceFt &&
                actualEnd.DistanceTo(expectedEnd) <= toleranceFt;
            bool reverseDirection =
                actualStart.DistanceTo(expectedEnd) <= toleranceFt &&
                actualEnd.DistanceTo(expectedStart) <= toleranceFt;
            if (!sameDirection && !reverseDirection)
            {
                throw new InvalidOperationException(
                    "O Revit alterou a posição do trecho horizontal após a criação. " +
                    "A operação foi cancelada para não deixar a união desalinhada.");
            }

            EnsureHorizontal(actualLine);
        }

        private static CornerSolution Calculate(
            Line first,
            Line second,
            double radiusFt,
            double shortCurveTolerance,
            bool validateExtension,
            int vertexAnchor)
        {
            return Calculate(
                first,
                second,
                radiusFt,
                shortCurveTolerance,
                validateExtension,
                vertexAnchor,
                null,
                null);
        }

        private static CornerSolution Calculate(
            Line first,
            Line second,
            double radiusFt,
            double shortCurveTolerance,
            bool validateExtension,
            int vertexAnchor,
            int? requestedFirstCornerEnd,
            int? requestedSecondCornerEnd)
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
                throw new RoundedCornerTransitionRequiredException(
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
                throw new RoundedCornerTransitionRequiredException(
                    $"Os eixos dos dois membros não se encontram " +
                    $"({axesDistance * MillimetersPerFoot:F1} mm de afastamento). " +
                    "Alinhe os eixos antes de arredondar o canto.");
            }

            var vertex = vertexAnchor == 0
                ? firstIntersection
                : vertexAnchor == 1
                    ? secondIntersection
                    : (firstIntersection + secondIntersection) * 0.5;

            int firstCornerEnd = ResolveCornerEnd(
                first,
                vertex,
                requestedFirstCornerEnd,
                "primeiro");
            int secondCornerEnd = ResolveCornerEnd(
                second,
                vertex,
                requestedSecondCornerEnd,
                "segundo");
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

        private static void EnsureHorizontal(Line line)
        {
            var direction = Direction(
                line.GetEndPoint(0),
                line.GetEndPoint(1),
                "intermediário");
            double inclinationDegrees =
                Math.Asin(Math.Min(1.0, Math.Abs(direction.Z))) * 180.0 / Math.PI;
            if (inclinationDegrees > 0.5)
            {
                throw new InvalidOperationException(
                    $"O trecho intermediário deve ser horizontal. " +
                    $"A inclinação encontrada foi {inclinationDegrees:F2} graus.");
            }
        }

        private static XYZ PointOnAxisAtElevation(
            Line axis,
            double elevation,
            string label)
        {
            if (axis == null)
                throw new ArgumentNullException(nameof(axis));
            var direction = axis.Direction;
            if (Math.Abs(direction.Z) <= 1e-9)
            {
                throw new InvalidOperationException(
                    $"O eixo do {label} não alcança a cota definida pela referência.");
            }

            var start = axis.GetEndPoint(0);
            double parameter = (elevation - start.Z) / direction.Z;
            var point = start + direction * parameter;
            if (!IsFinite(point))
            {
                throw new InvalidOperationException(
                    $"Não foi possível calcular o encontro do {label} com a cota da referência.");
            }
            return new XYZ(point.X, point.Y, elevation);
        }

        private static XYZ HorizontalDirection(Line axis, string label)
        {
            if (axis == null)
                throw new ArgumentNullException(nameof(axis));
            var direction = new XYZ(axis.Direction.X, axis.Direction.Y, 0.0);
            if (!IsFinite(direction) || direction.GetLength() <= 1e-9)
            {
                throw new InvalidOperationException(
                    $"O eixo do {label} não possui uma direção válida em planta.");
            }
            return direction.Normalize();
        }

        private static XYZ IntersectHorizontalSupportingLines(
            XYZ firstOrigin,
            XYZ firstDirection,
            XYZ secondOrigin,
            XYZ secondDirection,
            double elevation,
            string parallelMessage)
        {
            if (!IsFinite(firstOrigin) || !IsFinite(secondOrigin) ||
                !IsFinite(firstDirection) || !IsFinite(secondDirection))
            {
                throw new InvalidOperationException(
                    "Não foi possível calcular uma interseção horizontal finita para a rota.");
            }

            double determinant =
                firstDirection.X * secondDirection.Y -
                firstDirection.Y * secondDirection.X;
            if (Math.Abs(determinant) <= 1e-8)
                throw new InvalidOperationException(parallelMessage);

            var delta = secondOrigin - firstOrigin;
            double firstParameter =
                (delta.X * secondDirection.Y -
                 delta.Y * secondDirection.X) / determinant;
            var point = firstOrigin + firstDirection * firstParameter;
            if (!IsFinite(point) ||
                double.IsNaN(elevation) ||
                double.IsInfinity(elevation))
            {
                throw new InvalidOperationException(
                    "Não foi possível calcular uma interseção horizontal finita para a rota.");
            }
            return new XYZ(point.X, point.Y, elevation);
        }

        private static Line CalculateAutomaticMiddleAxis(
            Document document,
            Line firstLine,
            int firstCornerEnd,
            Line secondLine,
            int secondCornerEnd,
            Line referenceAxis)
        {
            ValidateHorizontalReference(referenceAxis);
            var firstPoint = firstLine.GetEndPoint(firstCornerEnd);
            var secondPoint = secondLine.GetEndPoint(secondCornerEnd);
            var firstDirection = Direction(
                firstPoint,
                firstLine.GetEndPoint(1 - firstCornerEnd),
                "primeiro");
            var secondDirection = Direction(
                secondPoint,
                secondLine.GetEndPoint(1 - secondCornerEnd),
                "segundo");
            var horizontalDirection = new XYZ(
                referenceAxis.Direction.X,
                referenceAxis.Direction.Y,
                0.0);
            if (!IsFinite(horizontalDirection) ||
                horizontalDirection.GetLength() <= 1e-9)
                throw new InvalidOperationException(
                    "A direção da aresta de referência não é válida em planta.");
            horizontalDirection = horizontalDirection.Normalize();

            // Solução recuperada do stash 78a71b7: os deslocamentos acontecem
            // exclusivamente sobre os dois eixos existentes. As duas equações
            // impõem cota igual e conector paralelo à direção XY da aresta.
            var horizontalNormal =
                XYZ.BasisZ.CrossProduct(horizontalDirection);
            double elevationDelta = secondPoint.Z - firstPoint.Z;
            double lateralDelta =
                horizontalNormal.DotProduct(secondPoint - firstPoint);
            double row1First = firstDirection.Z;
            double row1Second = -secondDirection.Z;
            double row2First =
                horizontalNormal.DotProduct(firstDirection);
            double row2Second =
                -horizontalNormal.DotProduct(secondDirection);
            double row1NormSquared =
                row1First * row1First + row1Second * row1Second;
            double row2NormSquared =
                row2First * row2First + row2Second * row2Second;
            double determinant =
                row1First * row2Second - row1Second * row2First;
            double determinantScale = Math.Max(
                1.0,
                Math.Sqrt(row1NormSquared * row2NormSquared));
            double determinantTolerance = 1e-10 * determinantScale;
            double constraintTolerance = Math.Max(
                document.Application.VertexTolerance,
                0.01 / MillimetersPerFoot);

            double firstShift;
            double secondShift;
            if (Math.Abs(determinant) > determinantTolerance)
            {
                firstShift =
                    (elevationDelta * row2Second -
                     row1Second * lateralDelta) / determinant;
                secondShift =
                    (row1First * lateralDelta -
                     elevationDelta * row2First) / determinant;
            }
            else
            {
                bool useFirstRow = row1NormSquared >= row2NormSquared;
                double dominantNormSquared =
                    useFirstRow ? row1NormSquared : row2NormSquared;
                if (dominantNormSquared <= 1e-20)
                {
                    firstShift = 0.0;
                    secondShift = 0.0;
                }
                else
                {
                    double dominantFirst =
                        useFirstRow ? row1First : row2First;
                    double dominantSecond =
                        useFirstRow ? row1Second : row2Second;
                    double dominantValue =
                        useFirstRow ? elevationDelta : lateralDelta;
                    firstShift =
                        dominantFirst * dominantValue / dominantNormSquared;
                    secondShift =
                        dominantSecond * dominantValue / dominantNormSquared;
                }

                double elevationResidual = Math.Abs(
                    row1First * firstShift +
                    row1Second * secondShift -
                    elevationDelta);
                double lateralResidual = Math.Abs(
                    row2First * firstShift +
                    row2Second * secondShift -
                    lateralDelta);
                if (elevationResidual > constraintTolerance ||
                    lateralResidual > constraintTolerance)
                    throw new InvalidOperationException(
                        "A direção horizontal informada não permite ligar os dois " +
                        "eixos na mesma cota. Escolha outra aresta de referência.");
            }

            if (double.IsNaN(firstShift) || double.IsInfinity(firstShift) ||
                double.IsNaN(secondShift) || double.IsInfinity(secondShift))
                throw new InvalidOperationException(
                    "Não foi possível calcular deslocamentos finitos para o patamar.");

            XYZ firstVertex =
                firstPoint + firstDirection * firstShift;
            XYZ secondVertex =
                secondPoint + secondDirection * secondShift;
            if (!IsFinite(firstVertex) || !IsFinite(secondVertex))
                throw new InvalidOperationException(
                    "Não foi possível calcular pontos finitos para o patamar.");

            double elevationResidualAfterSolve =
                Math.Abs(firstVertex.Z - secondVertex.Z);
            double lateralResidualAfterSolve = Math.Abs(
                horizontalNormal.DotProduct(secondVertex - firstVertex));
            if (elevationResidualAfterSolve > constraintTolerance ||
                lateralResidualAfterSolve > constraintTolerance)
                throw new InvalidOperationException(
                    "A solução do patamar não satisfez a direção horizontal informada.");

            double commonZ = (firstVertex.Z + secondVertex.Z) * 0.5;
            firstVertex = new XYZ(firstVertex.X, firstVertex.Y, commonZ);
            secondVertex = new XYZ(secondVertex.X, secondVertex.Y, commonZ);

            double minimumLength = Math.Max(
                document.Application.ShortCurveTolerance,
                1.0 / MillimetersPerFoot);
            if (firstVertex.DistanceTo(secondVertex) <= minimumLength)
            {
                throw new InvalidOperationException(
                    "O trecho horizontal automático ficaria curto demais. " +
                    "Use a união direta ou selecione um trecho existente.");
            }

            SagaLog.Write(
                $"Patamar por direção: shift1={firstShift * MillimetersPerFoot:F1}mm, " +
                $"shift2={secondShift * MillimetersPerFoot:F1}mm, " +
                $"comprimento={firstVertex.DistanceTo(secondVertex) * MillimetersPerFoot:F1}mm, " +
                $"direçãoRef=({horizontalDirection.X:F6},{horizontalDirection.Y:F6}), " +
                $"p1=({firstVertex.X * MillimetersPerFoot:F1}," +
                $"{firstVertex.Y * MillimetersPerFoot:F1}," +
                $"{firstVertex.Z * MillimetersPerFoot:F1}), " +
                $"p2=({secondVertex.X * MillimetersPerFoot:F1}," +
                $"{secondVertex.Y * MillimetersPerFoot:F1}," +
                $"{secondVertex.Z * MillimetersPerFoot:F1}).");
            return Line.CreateBound(firstVertex, secondVertex);
        }

        private static bool IsHorizontalAxis(Line line)
        {
            if (line == null || line.Length <= 1e-9) return false;
            double inclinationDegrees =
                Math.Asin(Math.Min(1.0, Math.Abs(line.Direction.Z))) *
                180.0 / Math.PI;
            return inclinationDegrees <= 0.5;
        }

        private static XYZ CalculateAxisIntersectionOnReference(
            Document document,
            Line memberAxis,
            Line referenceAxis,
            XYZ selectedEnd,
            string label)
        {
            var memberStart = memberAxis.GetEndPoint(0);
            var referenceStart = referenceAxis.GetEndPoint(0);
            var memberDirection = memberAxis.Direction;
            var referenceDirection = referenceAxis.Direction;
            double directionDot = Math.Max(
                -1.0,
                Math.Min(1.0, memberDirection.DotProduct(referenceDirection)));
            double denominator = 1.0 - directionDot * directionDot;
            double toleranceFt = Math.Max(
                AxisIntersectionToleranceMm / MillimetersPerFoot,
                document.Application.VertexTolerance);

            if (denominator < 1e-12)
            {
                var projected = referenceStart + referenceDirection *
                    (selectedEnd - referenceStart).DotProduct(referenceDirection);
                double distanceFt = selectedEnd.DistanceTo(projected);
                if (distanceFt <= toleranceFt)
                    return selectedEnd;

                throw new InvalidOperationException(
                    $"O eixo do {label} é paralelo ao patamar e está afastado " +
                    $"{distanceFt * MillimetersPerFoot:F1} mm. Ajuste a altura ou o " +
                    "deslocamento do patamar sem alterar o eixo original do corrimão.");
            }

            var delta = memberStart - referenceStart;
            double memberDeltaProjection = memberDirection.DotProduct(delta);
            double referenceDeltaProjection = referenceDirection.DotProduct(delta);
            double memberParameter =
                (directionDot * referenceDeltaProjection - memberDeltaProjection) /
                denominator;
            double referenceParameter =
                (referenceDeltaProjection -
                 directionDot * memberDeltaProjection) /
                denominator;
            var pointOnMember = memberStart + memberDirection * memberParameter;
            var pointOnReference = referenceStart +
                                   referenceDirection * referenceParameter;
            double axesDistanceFt = pointOnMember.DistanceTo(pointOnReference);
            if (axesDistanceFt > toleranceFt)
            {
                throw new InvalidOperationException(
                    $"O eixo do {label} não encontra o eixo horizontal do patamar " +
                    $"({axesDistanceFt * MillimetersPerFoot:F1} mm de afastamento). " +
                    "Ajuste a referência; a inclinação e a altura originais serão preservadas.");
            }

            // Usa o ponto pertencente ao eixo do membro. A diferença para o ponto da
            // referência está limitada à tolerância e nunca reposiciona a ponta fora
            // da direção original do corrimão.
            return pointOnMember;
        }

        private static void EnsureOriginalAxisWasPreserved(
            RoundedCornerMember member,
            Line originalAxis,
            string label)
        {
            var currentAxis = member.GetAxis();
            var originalStart = originalAxis.GetEndPoint(0);
            var originalDirection = originalAxis.Direction;
            double toleranceFt = Math.Max(
                0.1 / MillimetersPerFoot,
                member.Instance.Document.Application.VertexTolerance);
            double maximumDistanceFt = Math.Max(
                (currentAxis.GetEndPoint(0) - originalStart)
                    .CrossProduct(originalDirection)
                    .GetLength(),
                (currentAxis.GetEndPoint(1) - originalStart)
                    .CrossProduct(originalDirection)
                    .GetLength());
            if (maximumDistanceFt > toleranceFt)
            {
                throw new InvalidOperationException(
                    $"A operação alteraria o eixo original do {label} em " +
                    $"{maximumDistanceFt * MillimetersPerFoot:F1} mm. " +
                    "A união foi cancelada para preservar inclinação e altura.");
            }
        }

        private static void ValidateHorizontalReference(Line referenceAxis)
        {
            if (referenceAxis == null || referenceAxis.Length <= 1e-9)
                throw new InvalidOperationException(
                    "Selecione uma aresta reta e horizontal como referência.");

            double inclinationDegrees =
                Math.Asin(Math.Min(1.0, Math.Abs(referenceAxis.Direction.Z))) *
                180.0 / Math.PI;
            if (inclinationDegrees > 0.5)
            {
                throw new InvalidOperationException(
                    $"A aresta de referência deve ser horizontal. " +
                    $"A inclinação encontrada foi {inclinationDegrees:F2} graus.");
            }
        }

        private static void ValidateCompoundSpacing(
            Document document,
            RoundedCornerPlan firstPlan,
            RoundedCornerPlan secondPlan,
            int middleFirstCornerEnd)
        {
            if (firstPlan == null || secondPlan == null)
                throw new ArgumentNullException(nameof(firstPlan));
            if (!firstPlan.SecondId.Equals(secondPlan.FirstId))
                throw new InvalidOperationException(
                    "Os dois cantos não compartilham o mesmo trecho intermediário.");
            if (firstPlan.SecondCornerEnd == secondPlan.FirstCornerEnd)
                throw new InvalidOperationException(
                    "Os dois cantos tentariam usar a mesma extremidade do trecho horizontal.");

            ValidateCompoundSpacing(
                document,
                firstPlan.OriginalSecondLine,
                firstPlan.SecondTangent,
                secondPlan.FirstTangent,
                middleFirstCornerEnd);
        }

        private static void ValidateCompoundSpacing(
            Document document,
            Line middle,
            XYZ firstTangent,
            XYZ secondTangent,
            int middleFirstCornerEnd)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (middle == null || firstTangent == null || secondTangent == null)
                throw new ArgumentNullException(nameof(middle));
            ValidateCornerEnd(middleFirstCornerEnd, "trecho horizontal");

            var start = middle.GetEndPoint(0);
            var direction = Direction(start, middle.GetEndPoint(1), "intermediário");
            double firstStation =
                (firstTangent - start).DotProduct(direction);
            double secondStation =
                (secondTangent - start).DotProduct(direction);
            double remainingFt = middleFirstCornerEnd == 0
                ? secondStation - firstStation
                : firstStation - secondStation;
            double minimumRemainingFt = Math.Max(
                document.Application.ShortCurveTolerance,
                1.0 / MillimetersPerFoot);
            if (remainingFt <= minimumRemainingFt)
            {
                throw new InvalidOperationException(
                    "O raio informado faz os dois arredondamentos se sobreporem no " +
                    "trecho horizontal. Use um raio menor ou um patamar mais longo.");
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

        private static Line CloneBoundLine(Line line)
        {
            if (line == null)
                throw new ArgumentNullException(nameof(line));
            return Line.CreateBound(
                new XYZ(
                    line.GetEndPoint(0).X,
                    line.GetEndPoint(0).Y,
                    line.GetEndPoint(0).Z),
                new XYZ(
                    line.GetEndPoint(1).X,
                    line.GetEndPoint(1).Y,
                    line.GetEndPoint(1).Z));
        }

        private static int NearestEnd(Line line, XYZ point)
        {
            return line.GetEndPoint(0).DistanceTo(point) <=
                   line.GetEndPoint(1).DistanceTo(point)
                ? 0
                : 1;
        }

        private static int ResolveCornerEnd(
            Line line,
            XYZ vertex,
            int? requestedCornerEnd,
            string label)
        {
            if (!requestedCornerEnd.HasValue)
                return NearestEnd(line, vertex);

            ValidateCornerEnd(requestedCornerEnd.Value, label);
            int requested = requestedCornerEnd.Value;
            double requestedDistance = line.GetEndPoint(requested).DistanceTo(vertex);
            double oppositeDistance = line.GetEndPoint(1 - requested).DistanceTo(vertex);
            double toleranceFt = AxisIntersectionToleranceMm / MillimetersPerFoot;
            if (requestedDistance > oppositeDistance + toleranceFt)
            {
                throw new InvalidOperationException(
                    $"O ponto clicado indica a extremidade oposta do {label} perfil. " +
                    "Clique mais perto da ponta que deve receber a união.");
            }

            return requested;
        }

        private static void ValidateCornerEnd(int cornerEnd, string label)
        {
            if (cornerEnd != 0 && cornerEnd != 1)
                throw new InvalidOperationException(
                    $"A extremidade informada para {label} é inválida.");
        }

        private static bool IsFinite(XYZ point)
        {
            return point != null &&
                   !double.IsNaN(point.X) && !double.IsInfinity(point.X) &&
                   !double.IsNaN(point.Y) && !double.IsInfinity(point.Y) &&
                   !double.IsNaN(point.Z) && !double.IsInfinity(point.Z);
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

        internal static void CopyPlacementParameters(
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
