using System;

namespace SAGAStructuralTools.BasePlate.Domain
{
    public class BasePlateCalculator
    {
        public BasePlateCalculationResult Calculate(BasePlateInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var result = new BasePlateCalculationResult();

            ValidateNormalForces(result, input);
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
            input.TotalAnchors = CalculateTotalAnchors(input);

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

            AddPreliminaryResults(result, input);

            // TODO: Implementar formulas de espessura de placa.
            // TODO: Implementar verificacoes de nervuras.
            // TODO: Implementar pressoes e resistencia do concreto.
            // TODO: Implementar tracao, cisalhamento e interacao dos chumbadores.
            // TODO: Implementar verificacoes de aco da coluna e da placa.

            return result;
        }

        private static void ValidateNormalForces(
            BasePlateCalculationResult result,
            BasePlateInput input)
        {
            if (input.CompressionForceTf < 0)
            {
                result.Verifications.Add(new VerificationResult
                {
                    Name = "Nc",
                    Status = VerificationStatus.Failed,
                    Message = "Nc deve ser informado como valor positivo para compressão.",
                    CalculatedValue = input.CompressionForceTf,
                    RequiredValue = 0,
                    Unit = "tf"
                });
            }

            if (input.TensionForceTf > 0)
            {
                result.Verifications.Add(new VerificationResult
                {
                    Name = "Nt",
                    Status = VerificationStatus.Failed,
                    Message = "Nt deve ser informado como valor negativo para tração.",
                    CalculatedValue = input.TensionForceTf,
                    RequiredValue = 0,
                    Unit = "tf"
                });
            }

            if (input.CompressionForceTf == 0 && input.TensionForceTf == 0)
            {
                result.Verifications.Add(new VerificationResult
                {
                    Name = "Nc/Nt",
                    Status = VerificationStatus.Failed,
                    Message = "Ao menos um entre Nc e Nt deve ser diferente de zero.",
                    CalculatedValue = 0,
                    RequiredValue = 1,
                    Unit = "caso"
                });
            }
        }

        private static void AddPreliminaryResults(
            BasePlateCalculationResult result,
            BasePlateInput input)
        {
            double totalAnchors = Math.Max(1, CalculateTotalAnchors(input));
            double vsd = Math.Sqrt(input.ShearX_Tf * input.ShearX_Tf +
                input.ShearY_Tf * input.ShearY_Tf);
            double leverX = Math.Max(1, input.PlateLengthYmm - 2.0 * input.AnchorEdgeDistanceYmm) / 1000.0;
            double leverY = Math.Max(1, input.PlateLengthXmm - 2.0 * input.AnchorEdgeDistanceXmm) / 1000.0;
            double momentTension = Math.Max(0, input.MomentX_TfM) /
                (leverX * Math.Max(1, input.AnchorsX)) +
                Math.Max(0, input.MomentY_TfM) /
                (leverY * Math.Max(1, input.AnchorsY));

            double compressionCaseTension = Math.Max(
                0,
                momentTension - Math.Max(0, input.CompressionForceTf) / totalAnchors);
            double tensionCaseTension = Math.Max(
                0,
                (-input.TensionForceTf / totalAnchors) + momentTension);

            result.AnchorTensionTf = Math.Max(compressionCaseTension, tensionCaseTension);
            result.AnchorShearTf = vsd / totalAnchors;

            double plateAreaM2 = input.PlateLengthXmm * input.PlateLengthYmm / 1000000.0;
            result.ConcretePressureTfM2 = plateAreaM2 > 0
                ? Math.Max(0, input.CompressionForceTf) / plateAreaM2
                : 0;
            double ratio = input.ConcreteAreaRatioA2A1 > 0
                ? Math.Max(1, input.ConcreteAreaRatioA2A1)
                : 1;
            result.ConcreteResistanceTfM2 = input.ConcreteFckMpa * 101.971621 *
                Math.Min(2.0, Math.Sqrt(ratio));

            double cantileverX = Math.Max(0, (input.PlateLengthXmm - input.FlangeWidthMm) / 2.0);
            double cantileverY = Math.Max(0, (input.PlateLengthYmm - input.DepthMm) / 2.0);
            double pressureNmm2 = result.ConcretePressureTfM2 * 0.00980665;
            result.MinimumPlateThicknessMm = input.PlateFyMpa > 0
                ? Math.Sqrt(Math.Max(0, 6.0 * pressureNmm2 *
                    Math.Pow(Math.Max(cantileverX, cantileverY), 2) / input.PlateFyMpa))
                : 0;
            result.MinimumStiffenerThicknessMm = input.StiffenerHeightMm > 0
                ? Math.Max(0, input.StiffenerHeightMm / 20.0)
                : 0;
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
                Message = "Informe ao menos 2 chumbadores em cada direção.",
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

        private static int CalculateTotalAnchors(BasePlateInput input)
        {
            if (input.AnchorsX < 0 || input.AnchorsY < 0) return 0;
            return input.AnchorsX + input.AnchorsY;
        }
    }
}
