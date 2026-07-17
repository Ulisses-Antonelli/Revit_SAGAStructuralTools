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
                OriginalFirstLine = firstLine,
                OriginalSecondLine = secondLine,
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
            int secondCornerEnd)
        {
            CreateAutomaticCompoundPlan(
                document,
                firstId,
                firstCornerEnd,
                secondId,
                secondCornerEnd,
                0.1,
                0.0,
                false);
        }

        internal static RoundedCornerAutomaticCompoundPlan CreateAutomaticCompoundPlan(
            Document document,
            ElementId firstId,
            int firstCornerEnd,
            ElementId secondId,
            int secondCornerEnd,
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
            double radiusMm,
            double shortCurveTolerance,
            bool validateExtension)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (radiusMm <= 0 || double.IsNaN(radiusMm) || double.IsInfinity(radiusMm))
                throw new InvalidOperationException("Informe um raio maior que zero.");
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
                secondCornerEnd);

            var firstSolution = Calculate(
                firstLine,
                middleAxis,
                radiusMm / MillimetersPerFoot,
                shortCurveTolerance,
                validateExtension,
                -1,
                firstCornerEnd,
                0);
            var secondSolution = Calculate(
                middleAxis,
                secondLine,
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
                plan.RadiusMm);

            var first = RoundedCornerMember.Get(
                document,
                currentPlan.FirstId,
                "primeiro");
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

            var compoundPlan = CreateCompoundPlan(
                document,
                currentPlan.FirstId,
                currentPlan.FirstCornerEnd,
                middle.Id,
                middleFirstCornerEnd,
                currentPlan.SecondId,
                currentPlan.SecondCornerEnd,
                currentPlan.RadiusMm);
            var results = ApplyCompound(
                document,
                compoundPlan,
                middle.UniqueId);

            return new RoundedCornerAutomaticCompoundResult
            {
                MiddleElementId = middle.Id,
                CornerResults = results
            };
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

        private static Line CalculateAutomaticMiddleAxis(
            Document document,
            Line firstLine,
            int firstCornerEnd,
            Line secondLine,
            int secondCornerEnd)
        {
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

            double firstVertical = firstDirection.Z;
            double secondVertical = secondDirection.Z;
            double elevationDelta = secondPoint.Z - firstPoint.Z;
            double denominator =
                firstVertical * firstVertical +
                secondVertical * secondVertical;

            XYZ firstVertex;
            XYZ secondVertex;
            if (denominator < 1e-12)
            {
                double elevationTolerance = Math.Max(
                    document.Application.VertexTolerance,
                    0.01 / MillimetersPerFoot);
                if (Math.Abs(elevationDelta) > elevationTolerance)
                {
                    throw new InvalidOperationException(
                        $"Os dois eixos são horizontais e estão em cotas diferentes " +
                        $"({Math.Abs(elevationDelta) * MillimetersPerFoot:F1} mm). " +
                        "Selecione um trecho existente para definir a transição.");
                }

                firstVertex = firstPoint;
                secondVertex = secondPoint;
            }
            else
            {
                double firstShift =
                    firstVertical * elevationDelta / denominator;
                double secondShift =
                    -secondVertical * elevationDelta / denominator;
                firstVertex = firstPoint + firstDirection * firstShift;
                secondVertex = secondPoint + secondDirection * secondShift;
            }

            if (!IsFinite(firstVertex) || !IsFinite(secondVertex))
                throw new InvalidOperationException(
                    "Não foi possível calcular uma cota horizontal finita para o patamar.");

            // Neutraliza apenas o resíduo numérico da solução da restrição Z1 = Z2.
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

            return Line.CreateBound(firstVertex, secondVertex);
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
