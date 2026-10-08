using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SAGAStructuralTools.Tests
{
    public sealed class XamlBindingTests
    {
        [Theory]
        [InlineData("ForceUnit")]
        [InlineData("MomentUnit")]
        public void UnitRunBindingIsExplicitlyOneWay(string propertyName)
        {
            string path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "SAGAStructuralTools", "UI", "Strap",
                "ImportStrapReactionsWindow.xaml"));
            XDocument document = XDocument.Load(path);

            string expected = "{Binding " + propertyName + ", Mode=OneWay}";
            XElement run = document.Descendants()
                .Where(element => element.Name.LocalName == "Run")
                .Single(element => (string)element.Attribute("Text") == expected);

            Assert.NotNull(run);
        }
    }
}
