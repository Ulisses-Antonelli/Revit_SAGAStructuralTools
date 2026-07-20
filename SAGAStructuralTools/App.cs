using Autodesk.Revit.UI;
using SAGAStructuralTools.UI;
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
        private RailSelectionController _railSelectionController;
        private LadderSelectionController _ladderSelectionController;

        public Result OnStartup(UIControlledApplication application)
        {
            SagaLog.Write("=== App.OnStartup iniciado ===");
            try
            {
                const string tabName = "SAGA Tools";

                // CreateRibbonTab lança ArgumentException se a aba já existir na sessão
                try { application.CreateRibbonTab(tabName); }
                catch (Exception ex) { SagaLog.Write($"CreateRibbonTab: aba pode já existir ({ex.Message})"); }

                RibbonPanel panel;
                try { panel = application.CreateRibbonPanel(tabName, "Conversão IFC"); }
                catch (Exception ex)
                {
                    SagaLog.Exception("CreateRibbonPanel", ex);
                    return Result.Failed;
                }

                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                SagaLog.Write($"Assembly: {assemblyPath}");

                TryAddButton(panel, new PushButtonData(
                    name:         "ConvertIfc",
                    text:         "Converter IFC\npara Família",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.ConvertIfcCommand")
                {
                    ToolTip    = "Converte perfis metálicos de arquivos IFC em famílias estruturais nativas.",
                    LargeImage = LoadIcon("saga_32.png", 32),
                    Image      = LoadIcon("saga_16.png", 16)
                });

                TryAddButton(panel, new PushButtonData(
                    name:         "GenerateStair",
                    text:         "Gerar Escada\nMetálica",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.GenerateStairCommand")
                {
                    ToolTip    = "Gera automaticamente escadas metálicas com longarinas estruturais e degraus BIM.",
                    LargeImage = LoadIcon("stairs_32.png", 32),
                    Image      = LoadIcon("stairs_16.png", 16)
                });

                TryAddButton(panel, new PushButtonData(
                    name:         "GenerateRail",
                    text:         "Gerar Guarda-Corpo\nMetálico",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.GenerateRailCommand")
                {
                    ToolTip    = "Gera automaticamente guarda-corpos metálicos com montantes e corrimão estruturais.",
                    LargeImage = LoadIcon("railing_32.png", 32),
                    Image      = LoadIcon("railing_16.png", 16)
                });

                TryAddButton(panel, new PushButtonData(
                    name:         "GenerateLadder",
                    text:         "Escada\nMarinheiro",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.GenerateLadderCommand")
                {
                    ToolTip    = "Gera escadas marinheiro a partir da viga superior: montantes, degraus, suportes, gaiola e prolongamento. Edite com Alt+clique.",
                    LargeImage = LoadIcon("stairs_32.png", 32),
                    Image      = LoadIcon("stairs_16.png", 16)
                });

                TryAddButton(panel, new PushButtonData(
                    name:         "GenerateInclinedRail",
                    text:         "Guarda-Corpo\nInclinado",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.GenerateInclinedRailCommand")
                {
                    ToolTip    = "Gera guarda-corpos metálicos em linhas 3D ou vigas estruturais retas e inclinadas.",
                    LargeImage = LoadIcon("railing_32.png", 32),
                    Image      = LoadIcon("railing_16.png", 16)
                });

                TryAddButton(panel, new PushButtonData(
                    name:         "EditRail",
                    text:         "Editar\nGuarda-Corpo",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.EditRailCommand")
                {
                    ToolTip    = "Edita o guarda-corpo SAGA pré-selecionado ou solicita um membro. Também disponível com Alt+clique.",
                    LargeImage = LoadIcon("railing_32.png", 32),
                    Image      = LoadIcon("railing_16.png", 16)
                });

                TryAddButton(panel, new PushButtonData(
                    name:         "JoinHandrails",
                    text:         "Unir\nCorrimãos",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.JoinHandrailsCommand")
                {
                    ToolTip    = "Une perfis estruturais retos usados como corrimão, com um arco direto ou dois arcos através de um patamar horizontal.",
                    LargeImage = LoadIcon("railing_32.png", 32),
                    Image      = LoadIcon("railing_16.png", 16)
                });

                TryAddButton(panel, new PushButtonData(
                    name:         "RoundRailCorner",
                    text:         "Arredondar\nCanto",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.RoundRailCornerCommand")
                {
                    ToolTip    = "Une duas vigas ou uma viga e um pilar com um arco tangente.",
                    LargeImage = LoadIcon("railing_32.png", 32),
                    Image      = LoadIcon("railing_16.png", 16)
                });

                TryAddButton(panel, new PushButtonData(
                    name:         "RealAlign",
                    text:         "Alinhamento\nReal",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.RealAlignCommand")
                {
                    ToolTip    = "Estende o eixo real de um componente até uma referência (face, linha, ou eixo de outro membro) — diferente do Alinhar nativo, que só ajusta o recuo visual da junta.",
                    LargeImage = LoadIcon("railing_32.png", 32),
                    Image      = LoadIcon("railing_16.png", 16)
                });

                _railSelectionController = new RailSelectionController(application);
                _ladderSelectionController = new LadderSelectionController(application);

                SagaLog.Write("=== App.OnStartup concluído com sucesso ===");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                SagaLog.Exception("App.OnStartup", ex);
                return Result.Failed;
            }
        }

        private static void TryAddButton(RibbonPanel panel, PushButtonData data)
        {
            try
            {
                panel.AddItem(data);
                SagaLog.Write($"Botão OK: {data.Name}");
            }
            catch (Exception ex) { SagaLog.Exception($"AddItem({data.Name})", ex); }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            _railSelectionController?.Dispose();
            _railSelectionController = null;
            _ladderSelectionController?.Dispose();
            _ladderSelectionController = null;
            return Result.Succeeded;
        }

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
