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

                RibbonPanel generalPanel;
                RibbonPanel railPanel;
                try
                {
                    generalPanel = application.CreateRibbonPanel(tabName, "Ferramentas Estruturais");
                    railPanel = application.CreateRibbonPanel(tabName, "Guarda-Corpos");
                }
                catch (Exception ex)
                {
                    SagaLog.Exception("CreateRibbonPanel", ex);
                    return Result.Failed;
                }

                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                SagaLog.Write($"Assembly: {assemblyPath}");

                TryAddButton(generalPanel, new PushButtonData(
                    name:         "ConvertIfc",
                    text:         "Converter IFC\npara Família",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.ConvertIfcCommand")
                {
                    ToolTip    = "Converte perfis metálicos de arquivos IFC em famílias estruturais nativas.",
                    LargeImage = LoadIcon("saga_32.png", 32),
                    Image      = LoadIcon("saga_16.png", 16)
                });

                TryAddButton(generalPanel, new PushButtonData(
                    name:         "GenerateStair",
                    text:         "Gerar Escada\nMetálica",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.GenerateStairCommand")
                {
                    ToolTip    = "Gera automaticamente escadas metálicas com longarinas estruturais e degraus BIM.",
                    LargeImage = LoadIcon("stairs_32.png", 32),
                    Image      = LoadIcon("stairs_16.png", 16)
                });

                TryAddButton(generalPanel, new PushButtonData(
                    name:         "GenerateLadder",
                    text:         "Escada\nMarinheiro",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.GenerateLadderCommand")
                {
                    ToolTip    = "Gera escadas marinheiro a partir da viga superior: montantes, degraus, suportes, gaiola e prolongamento. Edite com Alt+clique.",
                    LargeImage = LoadIcon("stairs_32.png", 32),
                    Image      = LoadIcon("stairs_16.png", 16)
                });

                TryAddButton(generalPanel, new PushButtonData(
                    name:         "RealAlign",
                    text:         "Alinhamento\nReal",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.RealAlignCommand")
                {
                    ToolTip    = "Estende o eixo real de um componente até uma referência.",
                    LargeImage = LoadIcon("railing_32.png", 32),
                    Image      = LoadIcon("railing_16.png", 16)
                });

                TryAddButton(railPanel, new PushButtonData(
                    name:         "GenerateRail",
                    text:         "Horizontal",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.GenerateRailCommand")
                {
                    ToolTip    = "Gera guarda-corpos horizontais a partir de linhas ou vigas estruturais retas.",
                    LargeImage = LoadIcon("rail_generate_32.png", 32),
                    Image      = LoadIcon("rail_generate_16.png", 16)
                });

                TryAddButton(railPanel, new PushButtonData(
                    name:         "GenerateInclinedRail",
                    text:         "Inclinado",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.GenerateInclinedRailCommand")
                {
                    ToolTip    = "Gera guarda-corpos metálicos em linhas 3D ou vigas estruturais retas e inclinadas.",
                    LargeImage = LoadIcon("rail_inclined_32.png", 32),
                    Image      = LoadIcon("rail_inclined_16.png", 16)
                });

                railPanel.AddSeparator();

                var editRail = new PushButtonData(
                    "EditRail", "Editar Guarda-Corpo", assemblyPath,
                    "SAGAStructuralTools.Commands.EditRailCommand")
                {
                    ToolTip = "Edita o guarda-corpo SAGA pré-selecionado ou solicita um membro. Também disponível com Alt+clique.",
                    Image = LoadIcon("rail_edit_16.png", 16)
                };
                var joinHandrails = new PushButtonData(
                    "JoinHandrails", "Unir Corrimãos", assemblyPath,
                    "SAGAStructuralTools.Commands.JoinHandrailsCommand")
                {
                    ToolTip = "Une corrimãos com um arco direto ou dois arcos através de um patamar horizontal.",
                    Image = LoadIcon("rail_join_16.png", 16)
                };
                var roundCorner = new PushButtonData(
                    "RoundRailCorner", "Arredondar Canto", assemblyPath,
                    "SAGAStructuralTools.Commands.RoundRailCornerCommand")
                {
                    ToolTip = "Une duas vigas ou uma viga e um pilar com um arco tangente.",
                    Image = LoadIcon("rail_round_16.png", 16)
                };
                TryAddStackedButtons(railPanel, editRail, joinHandrails, roundCorner);

                var alignPosts = new PushButtonData(
                    "AlignRailPosts", "Alinhar Montantes", assemblyPath,
                    "SAGAStructuralTools.Commands.AlignRailPostsCommand")
                {
                    ToolTip = "Alinha um montante final ao plano transversal de outro, seguindo o eixo inclinado do guarda-corpo.",
                    Image = LoadIcon("rail_align_posts_16.png", 16)
                };
                var matchProperties = new PushButtonData(
                    "MatchRailProperties", "Igualar Propriedades", assemblyPath,
                    "SAGAStructuralTools.Commands.MatchRailPropertiesCommand")
                {
                    ToolTip = "Copia a configuração de um guarda-corpo SAGA para um ou mais destinos, preservando suas linhas-base.",
                    Image = LoadIcon("rail_match_16.png", 16)
                };
                var savePreset = new PushButtonData(
                    "SaveRailPreset", "Salvar Padrão", assemblyPath,
                    "SAGAStructuralTools.Commands.SaveRailPresetCommand")
                {
                    ToolTip = "Salva a configuração de um guarda-corpo SAGA como padrão reutilizável em outros projetos.",
                    Image = LoadIcon("railing_16.png", 16)
                };
                TryAddStackedButtons(
                    railPanel,
                    alignPosts,
                    matchProperties,
                    savePreset);

                TryAddButton(railPanel, new PushButtonData(
                    name:         "SplitBeam",
                    text:         "Interromper\nViga",
                    assemblyName: assemblyPath,
                    className:    "SAGAStructuralTools.Commands.SplitBeamCommand")
                {
                    ToolTip    = "Divide uma viga em duas no ponto de interseção com o eixo de uma viga de referência.",
                    LargeImage = LoadIcon("rail_round_32.png", 32),
                    Image      = LoadIcon("rail_round_16.png", 16)
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

        private static void TryAddStackedButtons(
            RibbonPanel panel,
            PushButtonData first,
            PushButtonData second)
        {
            try
            {
                panel.AddStackedItems(first, second);
                SagaLog.Write($"Botões compactos OK: {first.Name}, {second.Name}");
            }
            catch (Exception ex)
            {
                SagaLog.Exception(
                    $"AddStackedItems({first.Name}, {second.Name})",
                    ex);
            }
        }

        private static void TryAddStackedButtons(
            RibbonPanel panel,
            PushButtonData first,
            PushButtonData second,
            PushButtonData third)
        {
            try
            {
                panel.AddStackedItems(first, second, third);
                SagaLog.Write(
                    $"Botões compactos OK: {first.Name}, {second.Name}, {third.Name}");
            }
            catch (Exception ex)
            {
                SagaLog.Exception(
                    $"AddStackedItems({first.Name}, {second.Name}, {third.Name})",
                    ex);
            }
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
