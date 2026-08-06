using SAGAStructuralTools.Revit.Strap;
using SAGAStructuralTools.Strap.Domain;
using System.Collections.Generic;
using Xunit;

public sealed class ReactionWritePlanTests
{
    [Fact]
    public void ExternalChangeCreatesConflictAndPreventsTransaction()
    {
        var plan = Plan(Zero(), Reaction());
        var current = new Dictionary<long, ReactionValueSnapshot> { [10] = Snapshot(2) };
        var result = ReactionWritePreflight.Evaluate(plan, current);
        Assert.False(result.CanStartTransaction);
        Assert.NotEmpty(result.Conflicts);
    }

    [Fact]
    public void ExternalUpdateToDesiredSkipsEverySet()
    {
        var reaction = Reaction();
        var result = ReactionWritePreflight.Evaluate(Plan(Zero(), reaction),
            new Dictionary<long, ReactionValueSnapshot> { [10] = ReactionValueSnapshot.FromReaction(reaction) });
        Assert.True(result.CanStartTransaction);
        Assert.Empty(result.Operations);
        Assert.Equal(1, result.AlreadyUpdatedItems);
    }

    [Fact]
    public void EquivalentValuesProduceNoParameterOperation()
    {
        var reaction = Reaction();
        var desired = ReactionValueSnapshot.FromReaction(reaction);
        var nearly = new ReactionValueSnapshot(new[] { desired[0] + 0.5e-9, desired[1], desired[2], desired[3], desired[4], desired[5], desired[6] });
        Assert.Empty(ReactionWritePreflight.Evaluate(Plan(Zero(), reaction), new Dictionary<long, ReactionValueSnapshot> { [10] = nearly }).Operations);
    }

    [Fact]
    public void RolledBackResultNeverReportsPartialSuccess()
    {
        var result = new ReactionWriteResult(ReactionWriteStatus.RolledBack, 3, 0, 0, 0, null, "falha");
        Assert.Equal(0, result.UpdatedItems);
        Assert.Equal(0, result.ParameterWrites);
    }

    private static ReactionWritePlan Plan(ReactionValueSnapshot observed, ConsolidatedReaction reaction) => new ReactionWritePlan(new[] { new ReactionWritePlanItem(StrapTargetProfiles.StructuralColumns, 10, "001", reaction, observed) });
    private static ReactionValueSnapshot Zero() => Snapshot(0);
    private static ReactionValueSnapshot Snapshot(double first) => new ReactionValueSnapshot(new[] { first, 0, 0, 0, 0, 0, 0 });
    internal static ConsolidatedReaction Reaction() => new ConsolidatedReaction("001", 1, 2, 3, -1, 4, 5, 6);
}
