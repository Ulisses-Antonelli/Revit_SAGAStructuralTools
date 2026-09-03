using System;
using System.Collections.Generic;

namespace SAGAStructuralTools.BasePlate.Domain
{
    public sealed class BoltLayoutService
    {
        public IReadOnlyList<BoltPoint> GeneratePerimeterLayout(
            double plateLengthXmm,
            double plateLengthYmm,
            double edgeDistanceXmm,
            double edgeDistanceYmm,
            int anchorsX,
            int anchorsY)
        {
            ValidateLayoutInput(
                plateLengthXmm,
                plateLengthYmm,
                edgeDistanceXmm,
                edgeDistanceYmm,
                anchorsX,
                anchorsY);

            var points = new List<BoltPoint>();
            double spacingX = CalculateSpacingX(
                plateLengthXmm,
                edgeDistanceXmm,
                anchorsX);
            double spacingY = CalculateSpacingY(
                plateLengthYmm,
                edgeDistanceYmm,
                anchorsY);
            double left = -plateLengthXmm / 2.0 + edgeDistanceXmm;
            double right = plateLengthXmm / 2.0 - edgeDistanceXmm;
            double bottom = -plateLengthYmm / 2.0 + edgeDistanceYmm;
            double top = plateLengthYmm / 2.0 - edgeDistanceYmm;

            // IMPORTANT:
            // Nbx and Nby describe bolt positions ALONG THE PERIMETER.
            // This is NOT a filled Nx-by-Ny matrix.
            // Never replace this logic with Nbx * Nby.
            for (int i = 0; i < anchorsX; i++)
            {
                double x = left + i * spacingX;
                AddPoint(points, x, bottom, BoltSide.Bottom);
                AddPoint(points, x, top, BoltSide.Top);
            }

            for (int j = 1; j <= anchorsY - 2; j++)
            {
                double y = bottom + j * spacingY;
                AddPoint(points, left, y, BoltSide.Left);
                AddPoint(points, right, y, BoltSide.Right);
            }

            return points;
        }

        public static int CalculateTotalAnchors(int anchorsX, int anchorsY)
        {
            if (anchorsX < 2 || anchorsY < 2) return 0;
            return 2 * anchorsX + 2 * anchorsY - 4;
        }

        public static double CalculateSpacingX(
            double plateLengthXmm,
            double edgeDistanceXmm,
            int anchorsX)
        {
            if (anchorsX < 2) return 0;
            return (plateLengthXmm - 2.0 * edgeDistanceXmm) / (anchorsX - 1);
        }

        public static double CalculateSpacingY(
            double plateLengthYmm,
            double edgeDistanceYmm,
            int anchorsY)
        {
            if (anchorsY < 2) return 0;
            return (plateLengthYmm - 2.0 * edgeDistanceYmm) / (anchorsY - 1);
        }

        private static void AddPoint(
            ICollection<BoltPoint> points,
            double x,
            double y,
            BoltSide side)
        {
            points.Add(new BoltPoint
            {
                Index = points.Count + 1,
                X = x,
                Y = y,
                Side = side
            });
        }

        private static void ValidateLayoutInput(
            double plateLengthXmm,
            double plateLengthYmm,
            double edgeDistanceXmm,
            double edgeDistanceYmm,
            int anchorsX,
            int anchorsY)
        {
            if (plateLengthXmm <= 0) throw new ArgumentOutOfRangeException(nameof(plateLengthXmm));
            if (plateLengthYmm <= 0) throw new ArgumentOutOfRangeException(nameof(plateLengthYmm));
            if (edgeDistanceXmm <= 0) throw new ArgumentOutOfRangeException(nameof(edgeDistanceXmm));
            if (edgeDistanceYmm <= 0) throw new ArgumentOutOfRangeException(nameof(edgeDistanceYmm));
            if (anchorsX < 2) throw new ArgumentOutOfRangeException(nameof(anchorsX));
            if (anchorsY < 2) throw new ArgumentOutOfRangeException(nameof(anchorsY));
            if (plateLengthXmm <= 2.0 * edgeDistanceXmm)
                throw new ArgumentException("A placa deve permitir espaçamento positivo em X.");
            if (plateLengthYmm <= 2.0 * edgeDistanceYmm)
                throw new ArgumentException("A placa deve permitir espaçamento positivo em Y.");
        }
    }
}
