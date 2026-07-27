namespace SAGAStructuralTools.Strap.Domain
{
    public sealed class CombinationReaction
    {
        public CombinationReaction(string combinationId, ForceMoment reaction)
        {
            if (string.IsNullOrWhiteSpace(combinationId))
                throw new DomainValidationException("O identificador da combinação é obrigatório.");
            if (reaction == null)
                throw new DomainValidationException("A reação da combinação é obrigatória.");

            CombinationId = combinationId;
            Reaction = reaction;
        }

        public string CombinationId { get; }
        public ForceMoment Reaction { get; }
    }
}
