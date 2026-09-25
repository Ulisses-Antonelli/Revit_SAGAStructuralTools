using System;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Classifica viga/pilar pela geometria da barra (proporção, não depende de unidade) - o arquivo
    /// do Robot não marca isso explicitamente, e o tipo de perfil sozinho não é confiável (um HP, por
    /// exemplo, pode aparecer usado na horizontal no modelo real).
    /// </summary>
    public static class RobotMemberClassifier
    {
        private const double HorizontalRatioTolerance = 0.01; // ~0.6 grau de desaprumo

        public static bool IsColumn(RobotNode start, RobotNode end)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            double horizontal = Math.Sqrt(dx * dx + dy * dy);
            double total = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (total < 1e-9) return false;
            return horizontal / total < HorizontalRatioTolerance && Math.Abs(dz) > 1e-9;
        }

        public static bool IsColumn(RobotMember member) => IsColumn(member.Start, member.End);
    }
}
