using Autodesk.Revit.DB;
using SAGAStructuralTools.Revit.Strap;
using System;
using System.IO;
using Xunit;

public sealed class ReactionWriterCategoryGuardTests
{
    [Fact]
    public void PlanStoresProfileCategoryAndParameterSchema()
    {
        ReactionWritePlanItem item = Item(StrapTargetProfiles.StructuralColumns);
        Assert.Equal("structural-columns", item.TargetProfileId);
        Assert.Equal(BuiltInCategory.OST_StructuralColumns, item.ExpectedCategory);
        Assert.Equal("SGA_NO_PILAR", item.AssociationParameterName);
        Assert.Equal(7, item.ReactionParameterNames.Count);
    }

    [Fact]
    public void WriterGuardRejectsDifferentCurrentCategoryBeforeTransaction()
    {
        ReactionWritePlanItem item = Item(StrapTargetProfiles.StructuralColumns);
        Assert.False(ReactionWriteTargetGuard.MatchesCategory(item,
            (long)BuiltInCategory.OST_StructConnections));
    }

    [Fact]
    public void WriterGuardRejectsProfileSchemaMismatch()
    {
        ReactionWritePlanItem item = Item(StrapTargetProfiles.StructuralColumns);
        Assert.False(ReactionWriteTargetGuard.MatchesProfile(item,
            StrapTargetProfiles.StructuralConnections));
    }

    [Fact]
    public void WriterChecksCategoryBeforeOpeningTransaction()
    {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "SAGAStructuralTools", "Revit", "Strap", "StrapReactionWriter.cs"));
        string source = File.ReadAllText(path);
        int categoryGuard = source.IndexOf("MatchesCategory", StringComparison.Ordinal);
        int transaction = source.IndexOf("new Transaction", StringComparison.Ordinal);
        Assert.True(categoryGuard >= 0 && transaction > categoryGuard);
    }

    private static ReactionWritePlanItem Item(StrapTargetProfile profile)
        => new ReactionWritePlanItem(profile, 10, "001", ReactionWritePlanTests.Reaction(),
            new ReactionValueSnapshot(new double[7]));
}
