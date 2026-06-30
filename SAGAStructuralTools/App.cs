using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SAGAStructuralTools
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "SAGA Tools";
            application.CreateRibbonTab(tabName);

            var panel = application.CreateRibbonPanel(tabName, "Conversão IFC");
            var assemblyPath = Assembly.GetExecutingAssembly().Location;

            var buttonData = new PushButtonData(
                name:          "ConvertIfc",
                text:          "Converter IFC\npara Família",
                assemblyName:  assemblyPath,
                className:     "SAGAStructuralTools.Commands.ConvertIfcCommand")
            {
                ToolTip    = "Converte perfis metálicos de arquivos IFC em famílias estruturais nativas.",
                LargeImage = LoadIcon("saga_32.png", 32),
                Image      = LoadIcon("saga_16.png", 16)
            };

            panel.AddItem(buttonData);

            // Botão: Gerar Escada Metálica — mesma estrutura de ícone do botão existente
            var stairData = new PushButtonData(
                name:          "GenerateStair",
                text:          "Gerar Escada\nMetálica",
                assemblyName:  assemblyPath,
                className:     "SAGAStructuralTools.Commands.GenerateStairCommand")
            {
                ToolTip    = "Gera automaticamente escadas metálicas com longarinas estruturais e degraus BIM.",
                LargeImage = LoadIcon("saga_32.png", 32),  // substituir por ícone específico quando disponível
                Image      = LoadIcon("saga_16.png", 16)
            };
            panel.AddItem(stairData);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private static ImageSource LoadIcon(string fileName, int pixelSize)
        {
            try
            {
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                var fullPath    = Path.Combine(assemblyDir, "Resources", "Icons", fileName);
                if (!File.Exists(fullPath)) return null;

                // Carrega o PNG original (DPI pode estar errado)
                BitmapSource source;
                using (var stream = File.OpenRead(fullPath))
                {
                    var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    source = frame;
                }

                // Renderiza para RenderTargetBitmap que SEMPRE usa 96 DPI e pixelSize exato.
                // Isso corrige o DPI errado (~3 DPI) dos PNGs que causava exibição em 512px.
                var rect    = new Rect(0, 0, pixelSize, pixelSize);
                var visual  = new DrawingVisual();
                using (var ctx = visual.RenderOpen())
                    ctx.DrawImage(source, rect);

                var result = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
                result.Render(visual);
                result.Freeze();
                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}
