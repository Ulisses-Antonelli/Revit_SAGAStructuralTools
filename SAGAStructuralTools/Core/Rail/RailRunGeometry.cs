using Autodesk.Revit.DB;
using System;

namespace SAGAStructuralTools.Core.Rail
{
    /// <summary>
    /// Referencial geométrico de um trecho reto de guarda-corpo.
    /// As distâncias longitudinais são medidas sobre a linha 3D, enquanto alturas são
    /// sempre verticais globais e deslocamentos laterais permanecem horizontais.
    /// </summary>
    public sealed class RailRunGeometry
    {
        private const double MinimumLengthFt = 0.001;
        private const double MinimumHorizontalLengthFt = 0.001;
        private const double InclinationToleranceFt = 1.0 / 304.8; // 1 mm

        private RailRunGeometry(XYZ start, XYZ end)
        {
            Start = start;
            End = end;

            var vector = End - Start;
            LengthFt = vector.GetLength();
            Direction = vector.Normalize();

            var horizontal = new XYZ(vector.X, vector.Y, 0);
            HorizontalLengthFt = horizontal.GetLength();
            HorizontalDirection = horizontal.Normalize();
            Lateral = new XYZ(-HorizontalDirection.Y, HorizontalDirection.X, 0).Normalize();
        }

        public XYZ Start { get; }
        public XYZ End { get; }
        public XYZ Direction { get; }
        public XYZ HorizontalDirection { get; }
        public XYZ Lateral { get; }
        public double LengthFt { get; }
        public double LengthMm => LengthFt * 304.8;
        public double HorizontalLengthFt { get; }
        public double RiseFt => End.Z - Start.Z;
        public bool IsInclined => Math.Abs(RiseFt) > InclinationToleranceFt;

        /// <summary>
        /// Cria o referencial. Trechos inclinados são orientados do ponto mais baixo
        /// para o mais alto; trechos horizontais preservam a direção selecionada.
        /// </summary>
        public static RailRunGeometry Create(XYZ first, XYZ second)
        {
            if (first == null || second == null)
                throw new InvalidOperationException("A linha-base do guarda-corpo é inválida.");

            var vector = second - first;
            if (vector.GetLength() < MinimumLengthFt)
                throw new InvalidOperationException("A linha-base do guarda-corpo é muito curta.");

            var horizontal = new XYZ(vector.X, vector.Y, 0);
            if (horizontal.GetLength() < MinimumHorizontalLengthFt)
                throw new InvalidOperationException(
                    "A linha-base do guarda-corpo não pode ser vertical. Selecione uma linha reta com projeção horizontal.");

            double rise = second.Z - first.Z;
            if (Math.Abs(rise) <= InclinationToleranceFt)
                return new RailRunGeometry(
                    first,
                    new XYZ(second.X, second.Y, first.Z));

            if (first.Z > second.Z)
                return new RailRunGeometry(second, first);

            return new RailRunGeometry(first, second);
        }

        /// <summary>Retorna o ponto da linha-base a uma distância real ao longo da linha 3D.</summary>
        public XYZ PointAtDistanceMm(double distanceMm) =>
            Start + Direction * (distanceMm / 304.8);

        /// <summary>
        /// Retorna um ponto deslocado da linha-base. O offset vertical usa o eixo Z
        /// global e o lateral usa a normal horizontal unitária do trecho.
        /// </summary>
        public XYZ PointAtDistanceMm(double distanceMm, double verticalOffsetFt, double lateralOffsetFt) =>
            PointAtDistanceMm(distanceMm) + XYZ.BasisZ * verticalOffsetFt + Lateral * lateralOffsetFt;

        /// <summary>Cria uma linha paralela, transladada vertical e lateralmente.</summary>
        public RailRunGeometry Offset(double verticalOffsetFt, double lateralOffsetFt)
        {
            var translation = XYZ.BasisZ * verticalOffsetFt + Lateral * lateralOffsetFt;
            return new RailRunGeometry(Start + translation, End + translation);
        }

        /// <summary>Cria uma linha paralela usando deslocamentos informados em milímetros.</summary>
        public RailRunGeometry OffsetMm(double verticalOffsetMm, double lateralOffsetMm) =>
            Offset(verticalOffsetMm / 304.8, lateralOffsetMm / 304.8);
    }
}
