# SAGA Structural Tools — Arquitetura e Estrutura do Projeto

> Documento gerado a partir de análise do código-fonte em 2026-07-08 (branch `feature/metal-stair-generator`).

## 1. Visão Geral

**SAGA Structural Tools** é um add-in nativo para o **Autodesk Revit** (multi-target: 2023 e 2026) escrito em C#, que reúne três ferramentas para projetistas de estruturas metálicas:

| # | Ferramenta | Botão no Ribbon | Status |
|---|---|---|---|
| 1 | **Conversor IFC → Famílias Gerdau** | "Converter IFC para Família" | Funcional (v0.1–v0.3) |
| 2 | **Gerador de Escada Metálica** | "Gerar Escada Metálica" | Funcional (v0.4–v0.7, com patamar intermediário) |
| 3 | **Gerador de Guarda-Corpo Metálico** | "Gerar Guarda-Corpo Metálico" | Em desenvolvimento (WIP — montantes/corrimão OK, fechamento em quadro/travessas pendente) |

Todas as três compartilham a mesma DLL (`SAGAStructuralTools.dll`), a mesma aba do Ribbon ("SAGA Tools") e seguem o padrão **MVVM** com separação estrita entre lógica de domínio (sem dependência da API do Revit) e código de integração com o Revit.

---

## 2. Stack técnica

- **Linguagem:** C# 8.0
- **UI:** WPF (XAML) + code-behind mínimo
- **Multi-targeting** (`SAGAStructuralTools.csproj`):
  - `net48` → Revit 2023 (`RevitAPI.dll`/`RevitAPIUI.dll` referenciadas de `C:\Program Files\Autodesk\Revit 2023`)
  - `net8.0-windows` → Revit 2026 (referenciadas de um diretório local de DLLs 2026)
  - Ambas as referências são `Private=false` (não copiadas para o output — evita duplicar assemblies que o Revit já carrega)
- **Deploy automático pós-build**: o `.csproj` tem um target MSBuild (`DeployToRevit`) que copia a DLL, o `.addin` e os ícones para `C:\ProgramData\Autodesk\Revit\Addins\2023` ou `...\2026` conforme o `TargetFramework` compilado.
- **Registro do add-in:** `SAGAStructuralTools.addin` (manifesto XML lido pelo Revit na inicialização), aponta para `SAGAStructuralTools.App` como `IExternalApplication`.
- **Sem testes automatizados** no repositório atualmente. A camada `Core/Domain` foi desenhada para ser testável (zero dependência do Revit), mas nenhum projeto de teste existe ainda.

---

## 3. Estrutura de diretórios

```
SAGAStructuralTools/
├── App.cs                        # IExternalApplication — registra a aba/ribbon e os 3 botões
├── SagaLog.cs                    # Logger simples para arquivo (SAGA_Debug.txt), debug de crashes
├── SAGAStructuralTools.csproj    # multi-target net48/net8.0-windows + deploy automático
├── SAGAStructuralTools.addin     # manifesto do add-in Revit
│
├── Commands/                     # IExternalCommand — pontos de entrada dos botões do Ribbon
│   ├── ConvertIfcCommand.cs      # abre MainWindow (modal)
│   ├── GenerateStairCommand.cs   # abre StairWindow (não-modal)
│   └── GenerateRailCommand.cs    # abre RailWindow em thread STA dedicada (não-modal)
│
├── Core/                         # Lógica de negócio, sem UI
│   ├── ElementIdExtensions.cs    # helper para diferença de API ElementId entre net48/net8
│   ├── Conversion/
│   │   └── ElementConverter.cs   # substitui elementos IFC por famílias nativas (transação por elemento)
│   ├── Mapping/
│   │   ├── GerdauCatalog.cs      # indexa .rfa + catálogos .txt (de-para de nomes)
│   │   └── ProfileMatcher.cs     # regex + normalização de nomenclatura IFC → Gerdau
│   ├── Domain/                   # ★ cálculo puro, ZERO dependência de Revit — testável isoladamente
│   │   ├── StairCalculator.cs    # nº de degraus, espelho, pisada, patamares, inclinação
│   │   ├── StairDefaults.cs
│   │   ├── BlondelRule.cs        # regra de conforto de escadas (2h + p = 63~64cm)
│   │   ├── LandingCalculator.cs  # profundidade de patamares de extremidade
│   │   ├── RailCalculator.cs     # distribuição de montantes (por qtd / vão máx / eixo fixo)
│   │   └── RailDefaults.cs
│   ├── Stair/                    # construção de geometria da escada na API do Revit
│   │   ├── BeamPickHandler.cs        # ExternalEvent — seleção interativa de viga + ponto de clique
│   │   ├── StairCreationHandler.cs   # ExternalEvent — orquestra a criação em transação
│   │   ├── StringerBuilder.cs        # cria longarinas (vigas inclinadas), patamares, flip U/Canal
│   │   ├── TreadBuilder.cs           # cria degraus como DirectShape (sólido extrudado)
│   │   ├── LandingBuilder.cs         # cria a chapa do patamar intermediário (DirectShape)
│   │   └── ProfileGeometryReader.cs  # lê parâmetros do FamilySymbol p/ offset lateral correto (W/I vs U/Canal)
│   ├── Rail/                     # construção de geometria do guarda-corpo na API do Revit
│   │   ├── LinePickHandler.cs        # ExternalEvent — seleção de múltiplas linhas de perímetro
│   │   ├── RailCreationHandler.cs    # ExternalEvent — orquestra criação (posts + corrimão + fechamento)
│   │   ├── PostBuilder.cs            # cria montantes (viga ou pilar, conforme categoria da família)
│   │   ├── HandrailBuilder.cs        # cria o corrimão (viga inclinada/horizontal no topo dos montantes)
│   │   └── InfillBuilder.cs          # ★ STUB — travessas/quadro de fechamento ainda não implementados
│   └── Models/                   # DTOs/POCOs compartilhados entre Domain, Core e UI
│       ├── StairConfig.cs / StairDefinition.cs
│       ├── RailConfig.cs / RailDefinition.cs / RailSegment.cs / BarConfig.cs
│       ├── ProfileMapping.cs / ConversionResult.cs
│
├── UI/                            # WPF Views + ViewModels (MVVM)
│   ├── MainWindow.xaml(.cs)      # janela do conversor IFC (modal)
│   ├── StairWindow.xaml(.cs)     # janela do gerador de escada (não-modal)
│   ├── RailWindow.xaml(.cs)      # janela do gerador de guarda-corpo (não-modal, thread STA própria)
│   ├── Converters/
│   │   └── EnumToBoolConverter.cs  # bind de RadioButton ↔ enum (DistributionMode, InfillMode etc.)
│   └── ViewModels/
│       ├── ViewModelBase.cs      # INotifyPropertyChanged genérico
│       ├── RelayCommand.cs       # ICommand genérico (padrão MVVM clássico)
│       ├── MainViewModel.cs      # lógica do conversor IFC (catálogo, progresso, log)
│       ├── StairViewModel.cs     # lógica da escada (seleção de vigas, preview, criação)
│       ├── RailViewModel.cs      # lógica do guarda-corpo (seleção de linhas, abas de config, preview)
│       └── BarConfigVm.cs        # VM de linha da grade de travessas horizontais
│
└── Resources/Icons/              # ícones do Ribbon (PNG, 16px/32px) + logo
```

---

## 4. Padrão arquitetural

O projeto segue **MVVM** com uma 4ª camada extra ("Domain") explicitamente isolada da API do Revit:

```
Commands (IExternalCommand)
      │  abre janela, injeta UIApplication/ExternalEvents
      ▼
UI/Views (XAML)  ⇄  UI/ViewModels (estado, comandos, binding)
      │                     │
      │                     ▼
      │              Core/Domain (cálculo puro — sem Revit, testável)
      │                     │
      ▼                     ▼
Core/Stair, Core/Rail, Core/Conversion, Core/Mapping
      (IExternalEventHandler — únicas classes que tocam a Revit API a partir de janelas não-modais)
```

### Por que `ExternalEvent` / `IExternalEventHandler`?

Janelas WPF não-modais (`Show()`, como `StairWindow` e `RailWindow`) não rodam dentro do contexto de API válido do Revit — cliques de botão não são "eventos Revit". Todo acesso à `Document`/`Transaction` a partir dessas janelas passa por um `IExternalEventHandler` (`BeamPickHandler`, `StairCreationHandler`, `LinePickHandler`, `RailCreationHandler`), cujo `Execute(UIApplication)` só roda quando o Revit está pronto para receber chamadas de API. Isso está documentado em comentários no próprio código (`StairCreationHandler.cs:10-16`).

### Por que a `RailWindow` roda em thread STA dedicada?

`GenerateRailCommand.cs` cria a janela em uma `Thread` STA separada (com seu próprio `Dispatcher.Run()`), diferente da `StairWindow` que roda na thread principal do Revit. O comentário no código indica que isso evita um crash (`0xe0434352`) causado por conflito entre o pipeline de composição WPF/Direct3D e o pipeline de renderização do Revit quando compartilham a mesma thread. É uma decisão específica de estabilidade, não um padrão a repetir sem necessidade — `StairWindow` e `MainWindow` não precisam disso.

### `Core/Domain` — a camada "pura"

`StairCalculator`, `BlondelRule`, `LandingCalculator` e `RailCalculator` não importam `Autodesk.Revit.*`. Trabalham só com `double`/`int`/os `Models` (POCOs). Isso os torna unit-testáveis sem precisar do Revit rodando — hoje não há testes, mas a arquitetura já viabiliza isso.

---

## 5. As três funcionalidades em detalhe

### 5.1 Conversor IFC → Famílias Gerdau

**Fluxo:** `ConvertIfcCommand` → `MainWindow` (modal) → `MainViewModel`.

1. Usuário aponta um diretório local com famílias `.rfa` da Gerdau (persistido em `SAGAStructuralTools.settings`, ao lado da DLL).
2. `GerdauCatalog.Load()` varre o diretório recursivamente. Para cada `.rfa`:
   - Se existir um catálogo de tipos `.txt` homônimo (padrão Revit "type catalog"), indexa cada linha (nome do tipo + massa linear no campo 11 para perfis U).
   - Senão, indexa o próprio arquivo como família de tipo único.
   - Classifica automaticamente como viga ou pilar pelo nome do arquivo ("Pilar"/"Coluna" = pilar; "Viga" tem prioridade e nunca é pilar).
3. Usuário clica em converter. `MainViewModel.ConvertSync()`:
   - Detecta se há um `RevitLinkInstance` (IFC vinculado) ativo; senão usa o próprio documento host.
   - Coleta elementos estruturais (`DirectShape` em categorias de viga/pilar/genérico + `FamilyInstance` de pilar).
   - Para cada elemento, `ProfileMatcher` extrai a designação do perfil do nome (regex para métrico `W200x35.9`, U imperial `U8"x17.1` via massa linear, L imperial `L2"x3/16`) e normaliza para o padrão Gerdau.
   - `ElementConverter.ConvertAll()` processa cada elemento em sua **própria Transaction** (rollback isolado por elemento — uma falha não aborta o lote). Extrai o eixo geométrico do elemento original (via `LocationCurve` nativa, ou via geometria sólida/bounding box para `DirectShape` de IFC), carrega/ativa o `FamilySymbol` correspondente e cria uma nova `FamilyInstance` nativa na mesma posição/orientação.
4. Resultado por elemento (`Success` / `NotFound` / `GeometryError`) é logado na UI em tempo real e salvo em arquivo de log ao final.

### 5.2 Gerador de Escada Metálica

**Fluxo:** `GenerateStairCommand` → `StairWindow` (não-modal) → `StairViewModel`.

1. Usuário seleciona interativamente a viga inferior e a viga superior (`BeamPickHandler`, via `ExternalEvent`), clicando sobre elas na vista — o ponto de clique é projetado no eixo da viga para precisão. Há validação de que a viga "superior" está de fato acima em Z, e o ponto de conexão superior é realinhado para manter a escada reta em planta.
2. Usuário configura: largura, pisada, espessura do degrau, aplicação da Regra de Blondel, centralização, inclusão de degraus, patamar intermediário (opcional) e seleciona a família/tipo da longarina.
3. **Cálculo (`StairCalculator`, puro):**
   - Determina nº de degraus e altura de espelho a partir do desnível total (usa `TargetRiserHeight` como ponto de partida, ou `BlondelRule.BestFit` se Blondel estiver ativo).
   - Posiciona o patamar intermediário no degrau mais próximo do centro do lance.
   - Calcula profundidade dos patamares de extremidade (`LandingCalculator`), descontando o comprimento do patamar intermediário.
   - Valida se o desenvolvimento total cabe na distância entre vigas; gera warnings (patamares curtos, escada não cabe, etc.).
4. **Criação (`StairCreationHandler` → `StringerBuilder`):**
   - Cria as longarinas como vigas estruturais inclinadas (par esquerdo/direito), incluindo trechos de patamar inferior/superior e, se houver, o segmento do patamar intermediário.
   - Perfis tipo U/Canal recebem `FlipHand` via rotação de 180° no parâmetro "Rotação do corte transversal" no banzo direito, para ficarem costas-a-costas — com correção de justificação Z (Top↔Bottom) para manter a altura correta.
   - `ProfileGeometryReader` lê parâmetros do `FamilySymbol` (`bf`, `b`, `Aba`, etc.) para calcular o offset lateral correto entre o eixo da longarina e a face de referência do perfil.
   - `LandingBuilder` cria a chapa do patamar intermediário como `DirectShape`.
   - `TreadBuilder` cria cada degrau como `DirectShape` (placa retangular extrudada), com ajuste de posição para degraus após o patamar intermediário.
5. Toda a criação roda em uma única `Transaction`, com rollback total em caso de erro.

### 5.3 Gerador de Guarda-Corpo Metálico (WIP)

**Fluxo:** `GenerateRailCommand` → `RailWindow` (não-modal, thread STA própria) → `RailViewModel`.

1. Usuário seleciona múltiplas linhas de perímetro (`LinePickHandler`, `PickObjects` com `Enter` para confirmar).
2. Configuração dividida em abas: distribuição de montantes (por quantidade fixa / vão máximo / espaçamento de eixo fixo), família do montante, família do corrimão (altura, offset, justificação), fechamento (travessas horizontais ou quadro com cantoneira), terminais.
3. **Cálculo (`RailCalculator`, puro):** para cada segmento de linha, calcula os offsets de posição de cada montante conforme o modo de distribuição escolhido (`ByCount`, `MaxSpan`, `FixedAxis`).
4. **Criação (`RailCreationHandler`):**
   - `PostBuilder`: cria os montantes — suporta família de Viga (inclinada entre base e topo) ou de Pilar (com ajuste de offset de topo).
   - `HandrailBuilder`: cria o corrimão como viga horizontal/inclinada na altura configurada, com offset lateral e justificação.
   - `InfillBuilder`: **stub vazio** — travessas horizontais e fechamento em quadro (cantoneira) estão planejados para v0.2 mas ainda não implementados (`TODO` explícito no código).

---

## 6. Convenções e decisões notáveis do código

- **Unidades:** a API do Revit trabalha internamente em pés; todo o código de domínio/UI trabalha em **milímetros**, com conversão explícita `/ 304.8` ou `* 304.8` nos pontos de fronteira (builders).
- **Logging:** cada subsistema grava em um arquivo de texto próprio ao lado da DLL (`SAGA_Debug.txt`, `SAGA_StairLog.txt`, `SAGA_RailLog.txt`) — não há framework de logging, é `File.AppendAllText` direto, pensado para debugar crashes do add-in em produção sem depender de um debugger anexado.
- **ElementId multi-target:** `ElementId.IntegerValue` foi removido no Revit 2025+ em favor de `ElementId.Value` (long). `ElementIdExtensions.GetId()` abstrai isso via `#if NET8_0_OR_GREATER`.
- **Supressão de warnings do Revit:** `ElementConverter` usa um `IFailuresPreprocessor` (`SuppressRevitWarnings`) para descartar avisos não-fatais (ex: "viga fora da linha central") durante a conversão em lote.
- **Persistência de configuração simples:** caminho do catálogo e diretório de log do conversor IFC são salvos em texto plano (`SAGAStructuralTools.settings`, duas linhas) — não há um sistema de settings mais robusto.

---

## 7. Estado atual / pendências conhecidas

Com base no histórico de commits e no código:

- Conversor IFC: **maduro** (v0.1 → v0.3), com tratamento de casos de perfis inclinados, seção variável e pilares.
- Gerador de escada: **maduro** (v0.4 → v0.7), incluindo patamar intermediário completo (cálculo + geometria + UI).
- Gerador de guarda-corpo: **em progresso** — montantes e corrimão funcionam; fechamento (travessas/quadro) é stub (`InfillBuilder`); commit mais recente (`8ecbafd`) descreve como "WIP".
- **Sem suíte de testes automatizados** apesar da camada `Core/Domain` ser desenhada para isso.
- `Instruções_API_REVIT.md` na raiz contém a especificação original de MVP (em português) que motivou o conversor IFC — é a "bússola" original do projeto e ainda reflete fielmente a arquitetura MVVM implementada.