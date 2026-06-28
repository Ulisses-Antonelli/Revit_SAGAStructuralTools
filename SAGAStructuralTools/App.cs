using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace SAGAStructuralTools
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "SAGA Tools";
            application.CreateRibbonTab(tabName);

            var panel = application.CreateRibbonPanel(tabName, "Estrutural IFC");
            var assemblyPath = Assembly.GetExecutingAssembly().Location;

            var buttonData = new PushButtonData(
                name:          "ConvertIfc",
                text:          "Converter IFC\nGerdau",
                assemblyName:  assemblyPath,
                className:     "SAGAStructuralTools.Commands.ConvertIfcCommand")
            {
                ToolTip = "Converte perfis metálicos de arquivos IFC em famílias nativas do catálogo Gerdau.",
                LargeImage = LoadIcon("Resources\\Icons\\saga_32.png"),
                Image      = LoadIcon("Resources\\Icons\\saga_16.png")
            };

            panel.AddItem(buttonData);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private System.Windows.Media.ImageSource LoadIcon(string relativePath)
        {
            try
            {
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var fullPath = Path.Combine(assemblyDir, relativePath);
                if (!File.Exists(fullPath)) return null;

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(fullPath, UriKind.Absolute);
                image.EndInit();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}
