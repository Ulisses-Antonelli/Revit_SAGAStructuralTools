using System;

namespace SAGAStructuralTools.BasePlate.Domain
{
    public class BasePlateCalculator
    {
        public BasePlateCalculationResult Calculate(BasePlateInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var result = new BasePlateCalculationResult();

            ValidatePositive(result, "DepthMm", input.DepthMm, "mm");
            ValidatePositive(result, "FlangeWidthMm", input.FlangeWidthMm, "mm");
            ValidatePositive(result, "WebThicknessMm", input.WebThicknessMm, "mm");
            ValidatePositive(result, "FlangeThicknessMm", input.FlangeThicknessMm, "mm");
            ValidatePositive(result, "PlateFyMpa", input.PlateFyMpa, "MPa");
            ValidatePositive(result, "PlateFuMpa", input.PlateFuMpa, "MPa");
            ValidatePositive(result, "ConcreteFckMpa", input.ConcreteFckMpa, "MPa");
            ValidatePositive(result, "AnchorFyMpa", input.AnchorFyMpa, "MPa");
            ValidatePositive(result, "AnchorFuMpa", input.AnchorFuMpa, "MPa");
            ValidatePositive(result, "ColumnFyMpa", input.ColumnFyMpa, "MPa");
            ValidatePositive(result, "ColumnFuMpa", input.ColumnFuMpa, "MPa");
            ValidatePositive(result, "PlateLengthXmm", input.PlateLengthXmm, "mm");
            ValidatePositive(result, "PlateLengthYmm", input.PlateLengthYmm, "mm");
            ValidatePositive(result, "PlateThicknessMm", input.PlateThicknessMm, "mm");
            ValidatePositive(result, "AnchorDiameterMm", input.AnchorDiameterMm, "mm");
            ValidatePositive(result, "EmbedmentLengthMm", input.EmbedmentLengthMm, "mm");
            ValidateNonNegative(
                result,
                "CorrosionAllowanceMm",
                input.CorrosionAllowanceMm,
                "mm");
            ValidatePositive(
                result,
                "AnchorEdgeDistanceXmm",
                input.AnchorEdgeDistanceXmm,
                "mm");
            ValidatePositive(
                result,
                "AnchorEdgeDistanceYmm",
                input.AnchorEdgeDistanceYmm,
                "mm");
            ValidateNonNegative(
                result,
                "StiffenerThicknessMm",
                input.StiffenerThicknessMm,
                "mm");
            ValidateNonNegative(
                result,
                "StiffenerHeightMm",
                input.StiffenerHeightMm,
                "mm");

            AddComparison(
                result,
                "lx > bf",
                input.PlateLengthXmm,
                input.FlangeWidthMm,
                "mm",
                "Comprimento X da placa deve ser maior que a largura da mesa do perfil.");
            AddComparison(
                result,
                "ly > d",
                input.PlateLengthYmm,
                input.DepthMm,
                "mm",
                "Comprimento Y da placa deve ser maior que a altura do perfil.");

            AddAnchorCountVerification(result, "AnchorsX", input.AnchorsX);
            AddAnchorCountVerification(result, "AnchorsY", input.AnchorsY);

            double minimumEdgeDistance = 1.5 * input.AnchorDiameterMm;
            double edgeDistance = Math.Min(
                input.AnchorEdgeDistanceXmm,
                input.AnchorEdgeDistanceYmm);
            AddComparison(
                result,
                "Chumbador-borda",
                edgeDistance,
                minimumEdgeDistance,
                "mm",
                "Distancia minima entre chumbador e borda.");

            double minimumAnchorSpacing = 3.0 * input.AnchorDiameterMm;
            double spacingX = CalculateAnchorSpacing(
                input.PlateLengthXmm,
                input.AnchorEdgeDistanceXmm,
                input.AnchorsX);
            double spacingY = CalculateAnchorSpacing(
                input.PlateLengthYmm,
                input.AnchorEdgeDistanceYmm,
                input.AnchorsY);
            double anchorSpacing = Math.Min(spacingX, spacingY);
            AddComparison(
                result,
                "Chumbador-chumbador",
                anchorSpacing,
                minimumAnchorSpacing,
                "mm",
                "Espacamento minimo entre chumbadores.");

            result.CorrodedAnchorDiameterMm = Math.Max(
                0,
                input.AnchorDiameterMm - 2.0 * input.CorrosionAllowanceMm);

            // TODO: Implementar formulas de espessura de placa.
            // TODO: Implementar verificacoes de nervuras.
            // TODO: Implementar pressoes e resistencia do concreto.
            // TODO: Implementar tracao, cisalhamento e interacao dos chumbadores.
            // TODO: Implementar verificacoes de aco da coluna e da placa.

            return result;
        }

        private static void ValidatePositive(
            BasePlateCalculationResult result,
            string name,
            double value,
            string unit)
        {
            if (value > 0) return;
            result.Verifications.Add(new VerificationResult
            {
                Name = name,
                Status = VerificationStatus.Failed,
                Message = "Valor deve ser maior que zero.",
                CalculatedValue = value,
                RequiredValue = 0,
                Unit = unit
            });
        }

        private static void ValidateNonNegative(
            BasePlateCalculationResult result,
            string name,
            double value,
            string unit)
        {
            if (value >= 0) return;
            result.Verifications.Add(new VerificationResult
            {
                Name = name,
                Status = VerificationStatus.Failed,
                Message = "Valor nao pode ser negativo.",
                CalculatedValue = value,
                RequiredValue = 0,
                Unit = unit
            });
        }

        private static void AddAnchorCountVerification(
            BasePlateCalculationResult result,
            string name,
            int value)
        {
            result.Verifications.Add(new VerificationResult
            {
                Name = name,
                Status = value >= 2
                    ? VerificationStatus.Passed
                    : VerificationStatus.Failed,
                Message = "Quantidade de chumbadores deve ser maior ou igual a 2.",
                CalculatedValue = value,
                RequiredValue = 2,
                Unit = "un"
            });
        }

        private static void AddComparison(
            BasePlateCalculationResult result,
            string name,
            double calculated,
            double required,
            string unit,
            string message)
        {
            result.Verifications.Add(new VerificationResult
            {
                Name = name,
                Status = calculated > required
                    ? VerificationStatus.Passed
                    : VerificationStatus.Failed,
                Message = message,
                CalculatedValue = calculated,
                RequiredValue = required,
                Unit = unit
            });
        }

        private static double CalculateAnchorSpacing(
            double plateLength,
            double edgeDistance,
            int anchorCount)
        {
            if (anchorCount < 2) return 0;
            return (plateLength - 2.0 * edgeDistance) / (anchorCount - 1);
        }
    }
}
