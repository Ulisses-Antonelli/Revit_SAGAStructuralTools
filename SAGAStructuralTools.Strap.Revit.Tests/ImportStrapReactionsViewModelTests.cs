using SAGAStructuralTools.Revit.Strap;
using SAGAStructuralTools.UI.ViewModels.Strap;
using System;
using Xunit;

public sealed class ImportStrapReactionsViewModelTests
{
    [Fact]
    public void AlreadyUpdatedStartsUnselectedAndDisablesButton()
    {
        var reaction = ReactionWritePlanTests.Reaction();
        var row = Row("001", 1, reaction, ReactionValueSnapshot.FromReaction(reaction));
        var vm = Vm(row);
        Assert.True(row.IsAlreadyUpdated); Assert.False(row.IsSelected); Assert.False(vm.CanConfirm);
        Assert.Equal("Atualizar 0 elemento(s)", vm.ConfirmText);
    }

    [Fact]
    public void SelectPendingIncludesOnlyPendingRows()
    {
        var reaction = ReactionWritePlanTests.Reaction();
        var pending = Row("001", 1, reaction, new ReactionValueSnapshot(new double[7]));
        var updated = Row("002", 2, reaction, ReactionValueSnapshot.FromReaction(reaction));
        pending.IsSelected = false;
        var vm = Vm(pending, updated); vm.SelectPendingCommand.Execute(null);
        Assert.True(pending.IsSelected); Assert.False(updated.IsSelected); Assert.Equal(1, vm.SelectedForUpdateCount);
    }

    [Fact]
    public void RotationCanTurnUpdatedIntoPendingWithoutSelectingIt()
    {
        var reaction = ReactionWritePlanTests.Reaction();
        var row = Row("001", 1, reaction, ReactionValueSnapshot.FromReaction(reaction));
        row.Rotate90 = true;
        Assert.True(row.IsPending); Assert.True(row.CanSelect); Assert.False(row.IsSelected);
    }

    [Fact]
    public void SearchAndFilterDoNotChangeSelection()
    {
        var row = Row("001", 1, ReactionWritePlanTests.Reaction(), new ReactionValueSnapshot(new double[7]));
        var vm = Vm(row); bool selected = row.IsSelected;
        vm.SearchText = "zzz"; vm.ShowErrorsCommand.Execute(null);
        Assert.Equal(selected, row.IsSelected); Assert.Equal(0, vm.VisibleCount);
    }

    private static ImportStrapReactionsViewModel Vm(params StrapNodePreviewItem[] rows) => new ImportStrapReactionsViewModel("sample.rtf", "tf", "tf*metro", Array.Empty<string>(), rows);
    private static StrapNodePreviewItem Row(string nodeId, long elementId,
        SAGAStructuralTools.Strap.Domain.ConsolidatedReaction reaction, ReactionValueSnapshot values)
        => new StrapNodePreviewItem(nodeId, elementId, 1, reaction, Array.Empty<string>(),
            values, StrapTargetProfiles.StructuralColumns);
}
