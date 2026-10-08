using System;
using System.IO;
using Xunit;

public sealed class PreviewXamlTests
{
    [Fact]
    public void PreviewContainsRequiredActionsAndOneWayReadOnlyBindings()
    {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SAGAStructuralTools", "UI", "Strap", "ImportStrapReactionsWindow.xaml"));
        string xaml = File.ReadAllText(path);
        Assert.Contains("Selecionar pendentes", xaml);
        Assert.Contains("Desmarcar todas", xaml);
        Assert.Contains("ForceUnit, Mode=OneWay", xaml);
        Assert.Contains("MomentUnit, Mode=OneWay", xaml);
        Assert.Contains("Categoria de destino:", xaml);
        Assert.Contains("SelectedTargetProfile", xaml);
        Assert.Contains("SelectedRow.Errors", xaml);
        Assert.Contains("Nenhuma alteração será gravada para esta linha.", xaml);
        Assert.Contains("ToolTip", xaml);
    }
}
