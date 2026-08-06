using Autodesk.Revit.DB;
using SAGAStructuralTools.Revit.Strap;
using SAGAStructuralTools.UI.ViewModels.Strap;
using System;
using System.Collections.Generic;
using Xunit;

public sealed class StrapTargetCategoryTests
{
    [Fact]
    public void InitialStateHasNoCategoryAndNoFalseMappingError()
    {
        var vm = ViewModel();
        Assert.Null(vm.SelectedTargetProfile);
        Assert.False(vm.CanConfirm);
        Assert.Equal("Selecione a categoria de destino", vm.CategoryGuidance);
        Assert.True(vm.Rows[0].IsAwaitingCategory);
        Assert.Equal(0, vm.InvalidCount);
    }

    [Fact]
    public void InitialProfilesRepresentColumnsAndConnections()
    {
        Assert.Equal(BuiltInCategory.OST_StructuralColumns, StrapTargetProfiles.StructuralColumns.BuiltInCategory);
        Assert.Equal(BuiltInCategory.OST_StructConnections, StrapTargetProfiles.StructuralConnections.BuiltInCategory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MappingUsesOnlySelectedCategory(bool columns)
    {
        var profile = columns ? StrapTargetProfiles.StructuralColumns : StrapTargetProfiles.StructuralConnections;
        var candidate = Candidate(10, "3");
        StrapTargetMatch match = StrapTargetMapping.Match(profile, "3", new[] { candidate });
        Assert.Single(match.Candidates);
        Assert.Null(match.Error);
    }

    [Fact]
    public void SameKeyInDifferentCategoriesDoesNotConflict()
    {
        var catalog = new StrapCandidateCatalog(new[]
        {
            Entry(StrapTargetProfiles.StructuralColumns, Candidate(10, "3")),
            Entry(StrapTargetProfiles.StructuralConnections, Candidate(20, "3"))
        });
        Assert.Equal(10, Assert.Single(catalog.Get(StrapTargetProfiles.StructuralColumns)).ElementId);
        Assert.Equal(20, Assert.Single(catalog.Get(StrapTargetProfiles.StructuralConnections)).ElementId);
    }

    [Fact]
    public void DuplicateInsideSelectedCategoryIsAmbiguous()
    {
        StrapTargetMatch match = StrapTargetMapping.Match(StrapTargetProfiles.StructuralColumns,
            "3", new[] { Candidate(10, "3"), Candidate(11, "3") });
        Assert.True(match.IsDuplicate);
        Assert.Contains("Mais de um pilar estrutural", match.Error);
    }

    [Fact]
    public void MissingMessageNamesSelectedCategory()
    {
        var columns = StrapTargetMapping.Match(StrapTargetProfiles.StructuralColumns, "6", Array.Empty<StrapTargetCandidate>());
        var connections = StrapTargetMapping.Match(StrapTargetProfiles.StructuralConnections, "6", Array.Empty<StrapTargetCandidate>());
        Assert.Equal("Nenhum pilar estrutural corresponde ao SGA_NO_PILAR “6”.", columns.Error);
        Assert.Equal("Nenhuma conexão estrutural corresponde ao SGA_NO_PILAR “6”.", connections.Error);
    }

    [Fact]
    public void CategoryChangePreservesRotationSearchAndFilterButClearsSelection()
    {
        var vm = ViewModel();
        vm.SelectedTargetProfile = StrapTargetProfiles.StructuralColumns;
        vm.Rows[0].Rotate90 = true;
        vm.Rows[0].IsSelected = true;
        vm.SearchText = "3";
        vm.Filter = StrapPreviewFilter.Valid;

        vm.SelectedTargetProfile = StrapTargetProfiles.StructuralConnections;

        Assert.True(vm.Rows[0].Rotate90);
        Assert.False(vm.Rows[0].IsSelected);
        Assert.Equal("3", vm.SearchText);
        Assert.Equal(StrapPreviewFilter.Valid, vm.Filter);
        Assert.Equal(20, vm.Rows[0].ElementId);
    }

    private static ImportStrapReactionsViewModel ViewModel()
    {
        var reaction = ReactionWritePlanTests.Reaction();
        return new ImportStrapReactionsViewModel("sample.rtf", "tf", "tf*metro",
            Array.Empty<string>(), StrapTargetProfiles.All, profile =>
            {
                if (profile == null)
                    return new[] { new StrapNodePreviewItem("3", null, 1, reaction,
                        Array.Empty<string>(), isAwaitingCategory: true) };
                long id = profile == StrapTargetProfiles.StructuralColumns ? 10 : 20;
                return new[] { new StrapNodePreviewItem("3", id, 1, reaction,
                    Array.Empty<string>(), new ReactionValueSnapshot(new double[7]), profile) };
            });
    }

    private static StrapTargetCandidate Candidate(long id, string nodeId)
        => new StrapTargetCandidate(id, nodeId, Array.Empty<string>(), new ReactionValueSnapshot(new double[7]));
    private static KeyValuePair<string, IReadOnlyCollection<StrapTargetCandidate>> Entry(
        StrapTargetProfile profile, params StrapTargetCandidate[] candidates)
        => new KeyValuePair<string, IReadOnlyCollection<StrapTargetCandidate>>(profile.Id, candidates);
}
