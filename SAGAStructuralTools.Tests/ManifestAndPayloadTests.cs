using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SAGAStructuralTools.Tests
{
    public sealed class ManifestAndPayloadTests
    {
        [Fact]
        public void SourceManifest2023HasNoIsolationSettings()
        {
            XDocument document = XDocument.Load(Repo("SAGAStructuralTools", "SAGAStructuralTools.addin"));
            Assert.Empty(document.Root.Elements("ManifestSettings"));
            Assert.Empty(document.Root.Elements("AddIn").Elements("ManifestSettings"));
            Assert.Equal(@".\SAGAStructuralTools\SAGAStructuralTools.dll",
                document.Descendants("Assembly").Single().Value);
        }

        [Fact]
        public void SourceManifest2026HasNamedIsolatedContext()
        {
            XDocument document = XDocument.Load(Repo("SAGAStructuralTools", "SAGAStructuralTools.Revit2026.addin"));
            Assert.True(IsValid2026ManifestStructure(document));
            XElement settings = document.Root.Elements("ManifestSettings").Single();
            Assert.Equal("False", settings.Element("UseRevitContext")?.Value);
            Assert.Equal("SAGAStructuralTools", settings.Element("ContextName")?.Value);
            Assert.Equal(@".\SAGAStructuralTools\SAGAStructuralTools.dll",
                document.Descendants("Assembly").Single().Value);
        }

        [Fact]
        public void ManifestSettingsNestedInsideAddInIsRejected()
        {
            XDocument previousIncorrectStructure = XDocument.Parse(
                "<RevitAddIns>" +
                "<AddIn Type=\"Application\">" +
                "<ManifestSettings>" +
                "<UseRevitContext>False</UseRevitContext>" +
                "<ContextName>SAGAStructuralTools</ContextName>" +
                "</ManifestSettings>" +
                "</AddIn>" +
                "</RevitAddIns>");

            Assert.False(IsValid2026ManifestStructure(previousIncorrectStructure));
        }

        [Theory]
        [InlineData("Revit2023", false)]
        [InlineData("Revit2026", true)]
        public void ExistingStagingContainsTargetManifestAndPrivateDependencies(
            string stagingName, bool isolated)
        {
            string staging = Repo("artifacts", stagingName);
            Assert.True(Directory.Exists(staging), staging);
            string[] rootFiles = Directory.GetFiles(staging);
            Assert.Single(rootFiles);
            Assert.Equal("SAGAStructuralTools.addin", Path.GetFileName(rootFiles[0]));
            XDocument addin = XDocument.Load(rootFiles[0]);
            if (isolated)
                Assert.True(IsValid2026ManifestStructure(addin));
            else
            {
                Assert.Empty(addin.Root.Elements("ManifestSettings"));
                Assert.Empty(addin.Root.Elements("AddIn").Elements("ManifestSettings"));
            }

            string privateRoot = Path.Combine(staging, "SAGAStructuralTools");
            foreach (string required in new[] {
                "SAGAStructuralTools.dll",
                "SAGAStructuralTools.Strap.Domain.dll",
                "SAGAStructuralTools.Strap.Application.dll",
                "SAGAStructuralTools.Strap.Readers.dll",
                "DocumentFormat.OpenXml.dll",
                "System.Text.Encoding.CodePages.dll",
                "UglyToad.PdfPig.dll",
                "SAGAStructuralTools.payload.json" })
            {
                Assert.True(File.Exists(Path.Combine(privateRoot, required)), required);
            }
            Assert.False(File.Exists(Path.Combine(privateRoot, "RevitAPI.dll")));
            Assert.False(File.Exists(Path.Combine(privateRoot, "RevitAPIUI.dll")));
        }

        private static bool IsValid2026ManifestStructure(XDocument document)
        {
            if (document.Root == null || document.Root.Name != "RevitAddIns")
                return false;

            XElement[] directSettings = document.Root
                .Elements("ManifestSettings").ToArray();
            if (directSettings.Length != 1 ||
                document.Root.Elements("AddIn").Elements("ManifestSettings").Any())
                return false;

            XElement settings = directSettings[0];
            XElement[] useContext = settings.Elements("UseRevitContext").ToArray();
            XElement[] contextName = settings.Elements("ContextName").ToArray();
            return useContext.Length == 1 && contextName.Length == 1 &&
                useContext[0].Value == "False" &&
                contextName[0].Value == "SAGAStructuralTools";
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", ".."));
            return parts.Aggregate(root, Path.Combine);
        }
    }
}
