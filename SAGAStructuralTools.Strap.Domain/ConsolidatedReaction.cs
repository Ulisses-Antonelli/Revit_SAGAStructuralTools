namespace SAGAStructuralTools.Strap.Domain
{
    public sealed class ConsolidatedReaction
    {
        public ConsolidatedReaction(
            string nodeId,
            decimal x1,
            decimal x2,
            decimal x3Max,
            decimal x3Min,
            decimal x4,
            decimal x5,
            decimal x6)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new DomainValidationException("O número do nó é obrigatório.");
            if (x1 < 0m || x2 < 0m || x4 < 0m || x5 < 0m || x6 < 0m)
                throw new DomainValidationException(
                    "X1, X2, X4, X5 e X6 devem ser não negativos.");
            if (x3Max < x3Min)
                throw new DomainValidationException(
                    "X3_MAX deve ser maior ou igual a X3_MIN.");

            NodeId = nodeId;
            X1 = x1;
            X2 = x2;
            X3Max = x3Max;
            X3Min = x3Min;
            X4 = x4;
            X5 = x5;
            X6 = x6;
        }

        public string NodeId { get; }
        public decimal X1 { get; }
        public decimal X2 { get; }
        public decimal X3Max { get; }
        public decimal X3Min { get; }
        public decimal X4 { get; }
        public decimal X5 { get; }
        public decimal X6 { get; }

        public ConsolidatedReaction Rotate90()
            => new ConsolidatedReaction(
                NodeId,
                X2,
                X1,
                X3Max,
                X3Min,
                X5,
                X4,
                X6);
    }
}
