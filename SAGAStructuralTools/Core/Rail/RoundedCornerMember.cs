using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core;
using System;

namespace SAGAStructuralTools.Core.Rail
{
    internal enum RoundedCornerMemberKind
    {
        BeamCurve,
        ColumnCurve,
        VerticalPointColumn
    }

    /// <summary>
    /// Uniformiza o eixo e a edição das vigas e dos pilares que podem participar
    /// de um canto arredondado. Pilares verticais baseados em níveis não possuem
    /// LocationCurve; nesses casos o eixo é reconstruído a partir dos offsets.
    /// </summary>
    internal sealed class RoundedCornerMember
    {
        private const double MillimetersPerFoot = 304.8;
        private const double AxisToleranceMm = 0.5;

        private readonly string _label;

        private RoundedCornerMember(
            FamilyInstance instance,
            RoundedCornerMemberKind kind,
            string label)
        {
            Instance = instance;
            Kind = kind;
            _label = string.IsNullOrWhiteSpace(label) ? "selecionado" : label;
        }

        internal FamilyInstance Instance { get; }
        internal RoundedCornerMemberKind Kind { get; }
        internal bool IsBeam => Kind == RoundedCornerMemberKind.BeamCurve;
        internal bool IsColumn => !IsBeam;
        internal string StoredKind => Kind.ToString();

        internal static RoundedCornerMember Get(
            Document document,
            ElementId id,
            string label)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            var instance = document.GetElement(id) as FamilyInstance;
            return Create(instance, label, null);
        }

        internal static RoundedCornerMember GetStored(
            FamilyInstance instance,
            string storedKind,
            string label)
        {
            RoundedCornerMemberKind expectedKind;
            if (string.IsNullOrWhiteSpace(storedKind))
            {
                // Os registros anteriores à inclusão de pilares continham somente vigas.
                expectedKind = RoundedCornerMemberKind.BeamCurve;
            }
            else if (!Enum.TryParse(storedKind, out expectedKind) ||
                     !Enum.IsDefined(typeof(RoundedCornerMemberKind), expectedKind))
            {
                throw new InvalidOperationException(
                    "O registro do canto possui um tipo de membro desconhecido.");
            }

            return Create(instance, label, expectedKind);
        }

        internal static bool IsSelectable(Element element)
        {
            var instance = element as FamilyInstance;
            if (instance == null) return false;

            try
            {
                DetermineKind(instance, "selecionado");
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsBeamSelectable(Element element)
        {
            var instance = element as FamilyInstance;
            if (instance == null) return false;

            try
            {
                return DetermineKind(instance, "selecionado") ==
                       RoundedCornerMemberKind.BeamCurve;
            }
            catch
            {
                return false;
            }
        }

        private static RoundedCornerMember Create(
            FamilyInstance instance,
            string label,
            RoundedCornerMemberKind? expectedKind)
        {
            var kind = DetermineKind(instance, label);
            if (expectedKind.HasValue && expectedKind.Value != kind)
            {
                throw new InvalidOperationException(
                    "Um elemento ligado ao canto não corresponde mais ao tipo registrado.");
            }

            if (instance.Pinned)
                throw new InvalidOperationException(
                    $"O {label} perfil está fixado. Desafixe-o antes de arredondar o canto.");

            if (instance.GroupId != null &&
                !instance.GroupId.Equals(ElementId.InvalidElementId))
            {
                throw new InvalidOperationException(
                    $"O {label} perfil pertence a um grupo do Revit e não pode ser alterado isoladamente.");
            }

            var member = new RoundedCornerMember(instance, kind, label);
            member.GetAxis();
            return member;
        }

        private static RoundedCornerMemberKind DetermineKind(
            FamilyInstance instance,
            string label)
        {
            long categoryId = instance?.Category?.Id.GetId() ?? 0;
            if (categoryId == (int)BuiltInCategory.OST_StructuralFraming &&
                instance.StructuralType == StructuralType.Beam &&
                instance.Location is LocationCurve beamLocation &&
                beamLocation.Curve is Line)
            {
                return RoundedCornerMemberKind.BeamCurve;
            }

            if (categoryId == (int)BuiltInCategory.OST_StructuralColumns &&
                instance.StructuralType == StructuralType.Column)
            {
                if (instance.Location is LocationCurve columnLocation &&
                    columnLocation.Curve is Line)
                {
                    return RoundedCornerMemberKind.ColumnCurve;
                }

                if (instance.Location is LocationPoint)
                    return RoundedCornerMemberKind.VerticalPointColumn;
            }

            throw new InvalidOperationException(
                $"O {label} elemento deve ser uma viga reta ou um pilar estrutural reto.");
        }

        internal Line GetAxis()
        {
            if (Kind == RoundedCornerMemberKind.BeamCurve ||
                Kind == RoundedCornerMemberKind.ColumnCurve)
            {
                if (Instance.Location is LocationCurve location &&
                    location.Curve is Line line)
                {
                    return line;
                }

                throw new InvalidOperationException(
                    $"O {_label} perfil não possui mais um eixo reto válido.");
            }

            var pointLocation = Instance.Location as LocationPoint;
            if (pointLocation == null)
                throw new InvalidOperationException(
                    $"O {_label} pilar não possui um ponto de inserção válido.");

            var baseData = GetColumnEndData(0);
            var topData = GetColumnEndData(1);
            double baseZ = baseData.Level.ProjectElevation + baseData.Offset.AsDouble();
            double topZ = topData.Level.ProjectElevation + topData.Offset.AsDouble();
            double minimumLength = Math.Max(
                Instance.Document.Application.ShortCurveTolerance,
                0.1 / MillimetersPerFoot);
            if (topZ - baseZ <= minimumLength)
            {
                throw new InvalidOperationException(
                    $"O {_label} pilar não possui uma altura vertical válida.");
            }

            var insertion = pointLocation.Point;
            return Line.CreateBound(
                new XYZ(insertion.X, insertion.Y, baseZ),
                new XYZ(insertion.X, insertion.Y, topZ));
        }

        internal void EnsureCornerEndEditable(int cornerEnd)
        {
            ValidateEnd(cornerEnd);

            if (IsColumn)
            {
                var attachment = ColumnAttachment.GetColumnAttachment(Instance, cornerEnd);
                if (attachment != null)
                {
                    string endName = cornerEnd == 0 ? "base" : "topo";
                    throw new InvalidOperationException(
                        $"A {endName} do {_label} pilar está anexada a outro elemento. " +
                        "Desanexe-a antes de arredondar o canto.");
                }
            }

            if (Kind == RoundedCornerMemberKind.VerticalPointColumn)
            {
                var endData = GetColumnEndData(cornerEnd);
                if (endData.Offset.IsReadOnly)
                {
                    string endName = cornerEnd == 0 ? "base" : "topo";
                    throw new InvalidOperationException(
                        $"O offset da {endName} do {_label} pilar está bloqueado.");
                }
            }
        }

        internal bool IsJoinAllowedAtEnd(int cornerEnd)
        {
            ValidateEnd(cornerEnd);
            return IsBeam && StructuralFramingUtils.IsJoinAllowedAtEnd(Instance, cornerEnd);
        }

        internal void DisallowJoinAtEnd(int cornerEnd)
        {
            ValidateEnd(cornerEnd);
            if (IsBeam)
                StructuralFramingUtils.DisallowJoinAtEnd(Instance, cornerEnd);
        }

        internal void AllowJoinAtEnd(int cornerEnd)
        {
            ValidateEnd(cornerEnd);
            if (IsBeam)
                StructuralFramingUtils.AllowJoinAtEnd(Instance, cornerEnd);
        }

        internal void SetCornerEndpoint(int cornerEnd, XYZ point)
        {
            if (point == null)
                throw new ArgumentNullException(nameof(point));

            ValidateEnd(cornerEnd);
            EnsureCornerEndEditable(cornerEnd);
            var current = GetAxis();
            var other = current.GetEndPoint(1 - cornerEnd);
            double minimumLength = Instance.Document.Application.ShortCurveTolerance;
            if (point.DistanceTo(other) <= minimumLength)
            {
                throw new InvalidOperationException(
                    $"O ajuste deixaria o {_label} perfil menor que a tolerância do Revit.");
            }

            if (Kind == RoundedCornerMemberKind.BeamCurve ||
                Kind == RoundedCornerMemberKind.ColumnCurve)
            {
                var updated = cornerEnd == 0
                    ? Line.CreateBound(point, other)
                    : Line.CreateBound(other, point);
                ((LocationCurve)Instance.Location).Curve = updated;
                return;
            }

            var pointLocation = (LocationPoint)Instance.Location;
            var insertion = pointLocation.Point;
            double horizontalDistance = Math.Sqrt(
                Math.Pow(point.X - insertion.X, 2) +
                Math.Pow(point.Y - insertion.Y, 2));
            double axisTolerance = Math.Max(
                AxisToleranceMm / MillimetersPerFoot,
                Instance.Document.Application.VertexTolerance);
            if (horizontalDistance > axisTolerance)
            {
                throw new InvalidOperationException(
                    $"A tangência não coincide com o eixo vertical do {_label} pilar " +
                    $"({horizontalDistance * MillimetersPerFoot:F1} mm de diferença).");
            }

            if ((cornerEnd == 0 && point.Z >= other.Z - minimumLength) ||
                (cornerEnd == 1 && point.Z <= other.Z + minimumLength))
            {
                throw new InvalidOperationException(
                    $"O ajuste inverteria a base e o topo do {_label} pilar.");
            }

            var endData = GetColumnEndData(cornerEnd);
            if (!endData.Offset.Set(point.Z - endData.Level.ProjectElevation))
            {
                string endName = cornerEnd == 0 ? "base" : "topo";
                throw new InvalidOperationException(
                    $"O Revit não aceitou o novo offset da {endName} do {_label} pilar.");
            }
        }

        private ColumnEndData GetColumnEndData(int cornerEnd)
        {
            ValidateEnd(cornerEnd);
            BuiltInParameter levelParameterId = cornerEnd == 0
                ? BuiltInParameter.FAMILY_BASE_LEVEL_PARAM
                : BuiltInParameter.FAMILY_TOP_LEVEL_PARAM;
            BuiltInParameter offsetParameterId = cornerEnd == 0
                ? BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM
                : BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM;

            var levelParameter = Instance.get_Parameter(levelParameterId);
            var offsetParameter = Instance.get_Parameter(offsetParameterId);
            if (levelParameter == null ||
                levelParameter.StorageType != StorageType.ElementId ||
                offsetParameter == null ||
                offsetParameter.StorageType != StorageType.Double)
            {
                string endName = cornerEnd == 0 ? "base" : "topo";
                throw new InvalidOperationException(
                    $"Não foi possível ler o nível e o offset da {endName} do {_label} pilar.");
            }

            var level = Instance.Document.GetElement(levelParameter.AsElementId()) as Level;
            if (level == null)
            {
                string endName = cornerEnd == 0 ? "base" : "topo";
                throw new InvalidOperationException(
                    $"O nível da {endName} do {_label} pilar não é válido.");
            }

            return new ColumnEndData { Level = level, Offset = offsetParameter };
        }

        private static void ValidateEnd(int cornerEnd)
        {
            if (cornerEnd != 0 && cornerEnd != 1)
                throw new ArgumentOutOfRangeException(nameof(cornerEnd));
        }

        private sealed class ColumnEndData
        {
            internal Level Level { get; set; }
            internal Parameter Offset { get; set; }
        }
    }
}
