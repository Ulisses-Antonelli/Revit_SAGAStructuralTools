# SAGA Structural Tools — Arquitetura e Estrutura do Projeto

> Atualizado em 2026-08-14, a partir de análise completa do código-fonte na branch `develop`. Substitui a versão anterior (2026-07-08), que descrevia só 3 features e já estava bem defasada — faltavam `Core/Ladder`, `Core/Alignment`, as ferramentas de canto/união de guarda-corpo, e o guarda-corpo já não é mais WIP.

## 1. Visão geral

**SAGA Structural Tools** é um add-in para o Autodesk Revit (C#) que reúne ferramentas para projetistas de estruturas metálicas: conversão IFC → famílias Gerdau, geração de escada metálica reta, geração de guarda-corpo (reto/inclinado + ferramentas de edição: união de corrimãos, arredondamento de canto, alinhamento de montantes), geração de escada marinheiro com gaiola de proteção, e ferramentas avulsas de alinhamento (alinhamento real de eixo, alinhar ao ponto de trabalho, interromper viga).

O projeto começou como MVVM limpo (3 features: conversão IFC, escada, guarda-corpo). Ele cresceu bastante desde então, e o crescimento não foi uniforme — algumas features novas seguiram o mesmo padrão MVVM, outras foram implementadas como "comandos gordos" sem ViewModel. A Seção 5 documenta essa divergência explicitamente, e a Seção 6 lista os pontos onde nomenclatura e camadas ficaram confusas — é o material-base para a conversa sobre reorganização.

---

## 2. Estrutura de diretórios (atual)

```
SAGAStructuralTools/
├── App.cs                        # IExternalApplication — registra ribbon e todos os botões
├── SagaLog.cs                    # logger de arquivo (SAGA_Debug.txt) — mas ver nota na Seção 6.4
│
├── Commands/                     # IExternalCommand — pontos de entrada dos botões do Ribbon
│   ├── ConvertIfcCommand.cs
│   ├── GenerateStairCommand.cs
│   ├── GenerateRailCommand.cs / GenerateInclinedRailCommand.cs / EditRailCommand.cs
│   ├── GenerateLadderCommand.cs
│   ├── RealAlignCommand.cs / AlignToWorkPointCommand.cs / SplitBeamCommand.cs
│   ├── AlignRailPostsCommand.cs / MatchRailPropertiesCommand.cs / SaveRailPresetCommand.cs
│   ├── RoundRailCornerCommand.cs
│   └── JoinHandrailsCommand.cs   # 1525 linhas — o maior comando do repositório
│
├── Core/
│   ├── CatalogTextReader.cs      # solto, sem subpasta (ver Seção 6.5)
│   ├── ElementIdExtensions.cs    # solto, sem subpasta (ver Seção 6.5)
│   │
│   ├── Domain/                   # ★ cálculo puro — ZERO dependência de Revit, testável isoladamente
│   │   ├── StairCalculator.cs + StairDefaults.cs + BlondelRule.cs + LandingCalculator.cs
│   │   ├── RailCalculator.cs + RailDefaults.cs
│   │   └── LadderCalculator.cs + LadderDefaults.cs
│   │
│   ├── Models/                   # DTOs/POCOs — configs e resultados de cálculo
│   │   ├── StairConfig.cs / StairDefinition.cs
│   │   ├── RailConfig.cs / RailDefinition.cs / RailSegment.cs* / BarConfig.cs
│   │   ├── LadderConfig.cs / LadderDefinition.cs
│   │   └── ProfileMapping.cs / ConversionResult.cs
│   │   (*RailSegment.cs referencia Autodesk.Revit.DB — quebra a convenção "DTO puro" da pasta)
│   │
│   ├── Conversion/
│   │   └── ElementConverter.cs   # substitui elementos IFC por famílias nativas
│   ├── Mapping/
│   │   ├── GerdauCatalog.cs      # indexa .rfa + catálogos .txt
│   │   └── ProfileMatcher.cs     # regex + normalização IFC → Gerdau
│   │
│   ├── Stair/                    # geometria da escada reta na API do Revit
│   │   ├── BeamPickHandler.cs / StairCreationHandler.cs
│   │   └── StringerBuilder.cs / TreadBuilder.cs / LandingBuilder.cs / ProfileGeometryReader.cs
│   │
│   ├── Rail/                     # ★ a pasta mais heterogênea — 14 arquivos, ~4800 linhas
│   │   ├── LinePickHandler.cs / RailCreationHandler.cs
│   │   ├── PostBuilder.cs / HandrailBuilder.cs / InfillBuilder.cs
│   │   ├── RailAssemblyStore.cs / RailPresetStore.cs
│   │   ├── RailFamilySymbolResolver.cs / GeometryMeasure.cs / SectionSize.cs
│   │   ├── RailRunGeometry.cs
│   │   ├── RoundedCornerMember.cs / RoundedCornerStore.cs
│   │   └── RoundedCornerService.cs   # 2726 linhas — o maior arquivo do repositório
│   │
│   ├── Ladder/                   # geometria da escada marinheiro na API do Revit
│   │   ├── LadderPickHandler.cs / LadderCreationHandler.cs
│   │   ├── LadderBuilder.cs      # 723 linhas
│   │   ├── LadderAssemblyStore.cs / LadderPresetStore.cs
│   │   └── LadderPlacement.cs
│   │
│   └── Alignment/                # ferramentas avulsas de reposicionamento de elementos
│       ├── RealAlignmentService.cs
│       ├── WorkPointAlignmentService.cs
│       └── SplitBeamService.cs
│
└── UI/
    ├── MainWindow.xaml(.cs) / StairWindow.xaml(.cs) / RailWindow.xaml(.cs) / LadderWindow.xaml(.cs)
    ├── RoundedCornerWindow.xaml(.cs) / HandrailJoinWindow.xaml(.cs) / BatchHandrailJoinWindow.xaml(.cs)
    ├── SaveRailPresetWindow.xaml(.cs)
    ├── RailSelectionController.cs / LadderSelectionController.cs / RailToolSession.cs  # não são Views (Seção 6.7)
    ├── Converters/
    │   └── EnumToBoolConverter.cs / FlexibleDoubleConverter.cs / InverseBoolToVisibilityConverter.cs
    └── ViewModels/
        ├── ViewModelBase.cs / RelayCommand.cs
        ├── MainViewModel.cs / StairViewModel.cs
        ├── RailViewModel.cs      # 1172 linhas — o maior ViewModel
        ├── LadderViewModel.cs    # 738 linhas
        └── BarConfigVm.cs
```

---

## 3. Responsabilidade de cada pasta

| Pasta | O que contém, de fato |
|---|---|
| **`Commands/`** | Pontos de entrada `IExternalCommand`. Internamente inconsistente: metade só abre uma janela e retorna; a outra metade (`JoinHandrailsCommand`, `RoundRailCornerCommand`, `MatchRailPropertiesCommand`) contém laço de seleção, matemática de geometria, transação e diálogos — tudo dentro do comando. |
| **`Core/` (raiz)** | Dois utilitários genéricos sem feature dona (`CatalogTextReader`, `ElementIdExtensions`), soltos enquanto toda outra responsabilidade ganhou subpasta própria. |
| **`Core/Domain/`** | A única pasta que cumpre 100% o que o nome promete: cálculo puro, sem `using Autodesk.Revit.*` em lugar nenhum. `*Calculator` + `*Defaults` para escada reta, guarda-corpo e escada marinheiro. |
| **`Core/Models/`** | DTOs/POCOs (configs de entrada, resultados de cálculo), a maioria serializável em XML para presets. Quase todos sem dependência do Revit — exceto `RailSegment.cs`. |
| **`Core/Conversion/`** + **`Core/Mapping/`** | Suporte à conversão IFC: substituição de elementos (`ElementConverter`), indexação de catálogo Gerdau e casamento de nomenclatura IFC↔Gerdau. |
| **`Core/Stair/`** | Construção de geometria da escada reta: pick de vigas, builders de longarina/degrau/patamar, handler de criação. |
| **`Core/Rail/`** | A pasta mais carregada do projeto — 14 arquivos misturando quatro responsabilidades distintas sem separação por subpasta: persistência (`*AssemblyStore`, `*PresetStore`), construção de geometria (`*Builder`), orquestração de transação (`*CreationHandler`/`*PickHandler`), e o gigante `RoundedCornerService` (matemática de arco tangente + validação + criação de elemento, tudo junto). |
| **`Core/Ladder/`** | Mesmo padrão de `Core/Rail/` (persistência + builder + handlers), só que para a escada marinheiro — e sem o `RoundedCornerService`-sized problema. |
| **`Core/Alignment/`** | Três serviços pequenos e de responsabilidade única, cada um 1:1 com um `Commands/*Command` fino que só faz o laço de seleção e delega a matemática pra cá. É o canto mais bem organizado do `Core/`. |
| **`UI/` (raiz)** | Views (janelas WPF) — mas também três classes que não são Views (`RailSelectionController`, `LadderSelectionController`, `RailToolSession`): orquestram Alt+clique-para-editar e `ExternalEvent`, sem XAML nenhum. |
| **`UI/Converters/`** | Três `IValueConverter` pequenos e de responsabilidade única — a pasta mais "limpa" do projeto. |
| **`UI/ViewModels/`** | ViewModels MVVM — mas só das features que têm janela principal com `DataContext` (escada, guarda-corpo, escada marinheiro, conversor IFC). As ferramentas de edição (canto, união, alinhar montantes) não têm ViewModel — ver Seção 5. |

---

## 4. Padrão arquitetural (onde ele é seguido)

```
Commands (IExternalCommand)
      │  abre janela, injeta UIApplication/ExternalEvents
      ▼
UI/Views (XAML)  ⇄  UI/ViewModels (estado, RelayCommand, binding)
      │                     │
      │                     ▼
      │              Core/Domain (cálculo puro — sem Revit, testável)
      │                     │
      ▼                     ▼
Core/Stair, Core/Rail, Core/Ladder, Core/Conversion, Core/Mapping
      (IExternalEventHandler — únicas classes que tocam a API do Revit a partir de janelas não-modais)
```

Esse fluxo é real e funciona bem para **4 das features**: conversão IFC, escada reta, guarda-corpo (gerar/editar) e escada marinheiro. O `Core/Domain` cumpre a promessa de zero acoplamento ao Revit. Mas esse diagrama **não descreve o projeto inteiro** — as ferramentas de edição de guarda-corpo (união, canto, alinhar montantes) seguem um fluxo diferente, sem ViewModel. Ver Seção 5.

---

## 5. Census MVVM — onde o padrão é seguido e onde não é

| Feature | Padrão | Fluxo |
|---|---|---|
| Conversão IFC | MVVM completo | `ConvertIfcCommand` → `MainWindow` → `MainViewModel` → `ElementConverter`/`Mapping/*` |
| Escada reta | MVVM completo | `GenerateStairCommand` → `StairWindow` → `StairViewModel` → `StairCalculator` → `Core/Stair/*` |
| Guarda-corpo (gerar/editar) | MVVM completo | `RailWindow` → `RailViewModel` (1172 linhas) → `RailCalculator` → `PostBuilder`/`HandrailBuilder`/`InfillBuilder` |
| Escada marinheiro | MVVM completo | `LadderWindow` → `LadderViewModel` (738 linhas) → `LadderCalculator` → `LadderBuilder` |
| Alinhamento avulso (`RealAlignCommand`, `SplitBeamCommand`, `AlignToWorkPointCommand`) | **Sem ViewModel**, mas fino | Laço de seleção direto no `Execute()`, delega 100% da matemática pro `Core/Alignment/*Service` correspondente. Sem janela própria. |
| Canto/união de guarda-corpo (`RoundRailCornerCommand`, `JoinHandrailsCommand`, `AlignRailPostsCommand`, `MatchRailPropertiesCommand`) | **Sem ViewModel, comando gordo** | Laço de seleção + matemática + transação + diálogo, tudo dentro do `Commands/*.cs`. Janelas auxiliares (`RoundedCornerWindow`, `HandrailJoinWindow`) só pedem parâmetro, com validação no code-behind — não têm `DataContext`. |

**Isso não é aleatório — é uma divisão real:** "janela MVVM para *criar* uma peça nova" vs. "comando imperativo para *editar* uma peça existente". O problema é que essa divisão nunca foi escrita em lugar nenhum, então quem olha o projeto pela primeira vez lê como inconsistência, não como decisão.

---

## 6. Nomenclatura e camadas confusas — pontos concretos

Isso é o que embasa a sensação de "código não tão claro e objetivo" citada na conversa que motivou este documento.

**6.1 — `RoundedCornerService.cs` (2726 linhas) faz mais do que o nome promete.**
Um "Service" normalmente sugere uma camada fina. Este arquivo é simultaneamente: motor de matemática de arco tangente (4 modos de conexão), camada de validação (lança `InvalidOperationException` com texto de UI em português), camada de criação de elemento Revit, e um contêiner de ~10 tipos DTO aninhados (`RoundedCornerPlan`, `RoundedCornerCompoundPlan` etc.) que fariam mais sentido em `Core/Models`.

**6.2 — `JoinHandrailsCommand.cs` (1525 linhas) é o "comando gordo" mais extremo.**
Ao contrário de `RealAlignCommand`/`SplitBeamCommand`/`AlignToWorkPointCommand` (finos, delegam tudo pro `Core/Alignment`), este comando contém matemática de transformação de coordenadas, distância ponto-segmento, construção de eixo de referência, orquestração de `TransactionGroup` e fluxo de diálogo — tudo junto, sem um `Core/*Service` por trás.

**6.3 — Padrão "comando fino que delega" não é seguido por igual.**
`RealAlignCommand`/`SplitBeamCommand`/`AlignToWorkPointCommand` seguem esse padrão. `JoinHandrailsCommand`/`RoundRailCornerCommand`/`MatchRailPropertiesCommand` não — mesmo já existindo `RoundedCornerService` pra absorver parte dessa lógica.

**6.4 — Logging duplicado 5 vezes.**
`SagaLog.cs` existe como logger compartilhado, mas `LadderBuilder`, `LadderCreationHandler`, `HandrailBuilder`, `InfillBuilder`, `PostBuilder`, `StairCreationHandler`, `StringerBuilder`, `TreadBuilder` e `MainViewModel` cada um define seu próprio `Log()`/`LogPath` privado, escrevendo em arquivos separados (`SAGA_LadderLog.txt`, `SAGA_RailLog.txt`, `SAGA_StairLog.txt`...). Mesma responsabilidade, ~5 implementações independentes.

**6.5 — `RailAssemblyStore`/`LadderAssemblyStore` e `RailPresetStore`/`LadderPresetStore` são pares quase idênticos, copiados em vez de compartilhados.**
Mesmo formato (`Create`/`Attach`/`TryRead`/`FindMemberIds` de um lado; persistência de preset em XML do outro), GUID de schema diferente. Um bug corrigido num não se propaga pro outro automaticamente.

**6.6 — `Core/` tem dois arquivos órfãos sem subpasta.**
`CatalogTextReader.cs` e `ElementIdExtensions.cs` estão soltos na raiz de `Core/` enquanto toda outra responsabilidade — por menor que seja — ganhou pasta própria. `CatalogTextReader` só é usado por `Core/Mapping/GerdauCatalog.cs` e provavelmente pertence lá.

**6.7 — Controllers de seleção moram em `UI/` mas não são Views.**
`RailSelectionController`, `LadderSelectionController` e `RailToolSession` só lidam com `SelectionChanged`/`Idling`/`ExternalEvent` — zero XAML. Estão hoje na mesma pasta que janelas de verdade, misturando "apresentação" com "orquestração de sessão do Revit".

**6.8 — Janelas sem ViewModel fazem validação no code-behind.**
`HandrailJoinWindow`, `RoundedCornerWindow`, `BatchHandrailJoinWindow` e `SaveRailPresetWindow` não têm `*ViewModel` correspondente — parsing/validação de input vive direto no code-behind. Razoável pra um diálogo modal pequeno, mas não tem nenhuma pista de nomenclatura que avise "esta janela não segue MVVM" antes de abrir o arquivo.

**6.9 — `Core/Rail` e `Core/Ladder` misturam 4 responsabilidades sem subpasta.**
Persistência (`*AssemblyStore`), preset (`*PresetStore`), construção de geometria (`*Builder`) e orquestração de transação (`*CreationHandler`/`*PickHandler`) são todos arquivos irmãos, no mesmo nível. Quem quer achar "onde a geometria do guarda-corpo é de fato construída" precisa varrer os 14 arquivos de `Core/Rail/` sem nenhuma pista de pasta.

**6.10 — Três `*PickHandler` reimplementam o mesmo padrão sem base comum.**
`LinePickHandler`, `LadderPickHandler`, `BeamPickHandler` fazem a mesma coisa conceitual (selecionar um elemento, levantar um evento tipado) com assinaturas de evento diferentes (`LinePicked`/`PlacementPicked`/`BeamPicked`) e nenhuma interface compartilhada além do `IExternalEventHandler` do próprio Revit.

---

## 7. Convenções que já funcionam bem (vale preservar)

- **Unidades:** Revit trabalha em pés internamente; todo o domínio/UI trabalha em milímetros, convertendo explicitamente (`/ 304.8`, `* 304.8`) só nas bordas (builders). Consistente em todo o projeto.
- **`Core/Domain` sem dependência do Revit:** decisão respeitada à risca — nenhum `Calculator`/`Defaults` importa `Autodesk.Revit.*`. É o pedaço do projeto mais próximo de "testável de verdade" hoje.
- **`ElementIdExtensions.GetId()`:** abstrai a mudança de `ElementId.IntegerValue` → `ElementId.Value` entre versões do Revit num único lugar.
- **`Core/Alignment/*Service` + `Commands/*Command` fino:** o padrão mais limpo do repositório pra ferramentas pequenas — vale usar como modelo ao refatorar `JoinHandrailsCommand`/`RoundRailCornerCommand`.

---

## 8. Direção futura (não agora)

Registro da conversa, pra não se perder — **nada disto é pra ser executado já**, é só a direção que ficou combinada de "pensar":

- Organização por **feature folder** (cada feature — escada, guarda-corpo, escada marinheiro, conversão IFC, alinhamento — com sua própria pasta contendo View, ViewModel, Domain, Builder, Models, tudo junto), no espírito do que uma estrutura de projeto React/React Native costuma fazer, em vez de separar por "tipo técnico de arquivo" (`Core/`, `UI/`) como é hoje.
- Camadas inspiradas em **atomic design**: separar o que é "átomo" reaproveitável (ex.: `GeometryMeasure`, `SectionSize`, os `*PickHandler`, os conversores WPF) do que é específico de uma feature — hoje esses dois níveis estão misturados nas mesmas pastas.
- Eleger, aos poucos, o que vira **código realmente compartilhável** entre features (persistência de assembly, persistência de preset, logging, o padrão pick-handler) — hoje cada feature reimplementa sua própria versão em vez de um núcleo comum, no espírito do que um módulo `shared`/`common` faz num projeto KMP.
- Qualquer um desses pontos deve ser feito **incremental**, feature por feature, não como uma reescrita geral.
