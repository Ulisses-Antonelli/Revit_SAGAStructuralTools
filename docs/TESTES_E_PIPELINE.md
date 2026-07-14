# Orientação: Testes Automatizados, Pipeline CI/CD e Geração de Instalador

> Este documento é **orientação/planejamento**, não implementação. Nada aqui foi criado no
> repositório ainda (sem projeto de testes, sem workflow de CI, sem instalador). Serve como guia
> para quando decidirmos construir isso.

Relacionado: [ARQUITETURA.md](ARQUITETURA.md) (estrutura do código que este plano assume).

---

## 0. Bloqueio a resolver primeiro: referências ao RevitAPI não são portáveis

Antes de testes ou pipeline, existe um problema estrutural em `SAGAStructuralTools.csproj`:

```xml
<Reference Include="RevitAPI">
  <HintPath>C:\Program Files\Autodesk\Revit 2023\RevitAPI.dll</HintPath>
</Reference>
...
<HintPath>C:\Users\Ulisses\Desktop\dll_REVIT_2026\RevitAPI.dll</HintPath>
```

Esses caminhos só existem na sua máquina. Qualquer CI (ou outro dev) que tentar `dotnet build` falha
imediatamente, porque o projeto inteiro (não só os testes) depende dessas DLLs para compilar —
mesmo o código de `Core/Domain`, que não usa a API do Revit, está na mesma assembly que usa.

**Duas soluções, não mutuamente exclusivas:**

1. **Pacotes NuGet de referência do Revit API** (recomendado para CI). A comunidade Revit mantém
   pacotes como `Nice3point.Revit.Api.RevitAPI` / `Nice3point.Revit.Api.RevitAPIUI` (versionados
   por ano do Revit — 2023.x, 2026.x), que são assemblies de referência apenas para compilação
   (não redistribuem a implementação real, então não violam licença). Trocando os `<Reference>`
   fixos por `<PackageReference>` versionado por `TargetFramework`, o `dotnet restore` resolve tudo
   sozinho — em qualquer máquina, incluindo um runner de CI hospedado (não precisa Revit instalado
   para *compilar*).
2. **`Directory.Build.props` com caminho configurável por variável de ambiente**, para quem prefere
   manter os DLLs reais localmente (ex: para poder inspecionar API não documentada). Resolve o
   problema "funciona só na minha máquina", mas não resolve CI hospedado (o runner ainda precisaria
   ter os DLLs disponíveis de algum jeito — copiados via secret/artifact interno, por exemplo).

Recomendação: ir de vez para a opção 1. É o padrão que a maioria dos plugins Revit open-source usa
hoje para ter CI funcional.

---

## 1. Testes automatizados

### 1.1 Onde focar primeiro: `Core/Domain` e `Core/Mapping`

Esses dois diretórios já são "puros" — não importam `Autodesk.Revit.*`, só usam
`System`/`System.Linq`/`System.Text.RegularExpressions`. São o alvo ideal e imediato de testes
unitários, e é exatamente onde mora a lógica de **validação de input** que você mencionou:

| Classe | O que testar (casos de borda / input inválido) |
|---|---|
| `StairCalculator` | `totalRise <= 0`, `beamDistance <= 0` → deve marcar `IsValid=false` e gerar warning, não lançar exceção. Desenvolvimento total > distância entre vigas. Patamar intermediário habilitado com poucos degraus (`steps/2 - 1` negativo). |
| `BlondelRule` | `BestFit` com `totalRise` muito pequeno (poucos degraus possíveis), verificar que nunca retorna `steps < 2`. Casos onde nenhuma combinação em ±3 degraus satisfaz Blondel (deve cair no fallback central). |
| `LandingCalculator` | `gap <= 0` (escada maior que o vão) → deve retornar `(0,0)` sem lançar. Patamar intermediário consumindo todo o espaço disponível. `CenterStair=true` vs `false`. |
| `RailCalculator` | Lista de segmentos vazia/nula → `IsValid=false`. Segmento com `Length < 1.0mm`. Os 3 `DistributionMode` (`ByCount` com `count<2`, `MaxSpan`, `FixedAxis` com `step` muito pequeno). |
| `ProfileMatcher.Normalize` | Nomes malformados: sublinhado decimal (`22_5`→`22.5`), minúsculo (`200x22`), espaços extras, símbolo de polegada (`2"x`). Casos que **não** devem casar com nenhum regex (perfil totalmente desconhecido → deve retornar `null`, não lançar). |
| `GerdauCatalog.Load` | Diretório inexistente → não deve lançar (hoje já retorna cedo, bom caso de teste de regressão). Catálogo `.txt` corrompido/linhas com campos faltando. Duplicidade de nome de tipo (deve manter o primeiro, `ContainsKey` guard). |

Esses são exatamente os pontos onde um input mal formado hoje pode gerar um resultado silenciosamente
errado em vez de um erro claro — por isso priorizar aqui primeiro.

### 1.2 Estrutura de projeto sugerida

```
SAGAStructuralTools.sln
├── SAGAStructuralTools/            (projeto atual, plugin)
└── SAGAStructuralTools.Tests/      (novo — xUnit)
```

- Framework: **xUnit** (`Microsoft.NET.Test.Sdk` + `xunit` + `xunit.runner.visualstudio`) — é o padrão
  de fato em projetos .NET modernos, roda bem em `dotnet test` e em qualquer CI.
- Target: `net8.0` é suficiente para o projeto de teste (não precisa ser `net48` nem `-windows`,
  já que `Core/Domain` e `Core/Mapping` não usam WPF nem Revit).
- Referencia `SAGAStructuralTools.csproj` via `<ProjectReference>`. Uma vez resolvido o item 0
  (RevitAPI via NuGet), isso compila sem exigir Revit instalado.
- Nomeação de teste sugerida: `Given_<situação>_Should_<resultado esperado>` ou o padrão mais simples
  `MethodName_Scenario_ExpectedResult` — qualquer um serve, o importante é consistência.

### 1.3 O que **não** dá para testar assim: os *Builders* e *Handlers*

`StringerBuilder`, `TreadBuilder`, `PostBuilder`, `ElementConverter` etc. chamam `Document.Create`,
`Transaction`, `FilteredElementCollector` — só existem dentro de uma sessão real do Revit rodando.
Não são mockáveis de forma prática (a maior parte das classes da Revit API é `sealed` ou não tem
interface pública).

Duas rotas, em ordem de esforço:

1. **Checklist de teste manual** (baixo esforço, comece por aqui): um documento
   (`docs/TESTE_MANUAL.md`, a criar quando formos fazer isso) listando cenários a validar
   manualmente no Revit antes de cada release — ex: "escada com patamar intermediário + perfil U",
   "guarda-corpo com FixedAxis e segmento < step", "conversão IFC com pilar inclinado". Não é
   automação, mas documenta o que precisa ser conferido e vira base para depois automatizar.
2. **Revit Test Framework (RTF)** ou abordagem equivalente (mais esforço, investimento futuro):
   roda testes **dentro** de um processo real do Revit, com uma licença instalada. Isso exige um
   runner de CI **self-hosted** (uma máquina Windows sua/da empresa com Revit instalado, registrada
   como runner do GitHub Actions) — não roda em runner hospedado padrão (GitHub-hosted), que não
   tem Revit. Vale a pena quando o custo de bugs de geometria em produção justificar o investimento;
   não é pré-requisito para começar.

---

## 2. Pipeline CI (GitHub Actions)

O repositório já está no GitHub (`Ulisses-Antonelli/Revit_SAGAStructuralTools`), sem workflow hoje.
Estrutura sugerida em `.github/workflows/ci.yml` (ilustrativo — não criar ainda):

```yaml
name: CI
on:
  pull_request:
  push:
    branches: [main, "feature/**"]

jobs:
  test:
    runs-on: windows-latest        # precisa Windows por causa de WPF/net48
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
      - run: dotnet test SAGAStructuralTools.Tests --configuration Release

  build:
    needs: test
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
      - run: dotnet restore
      - run: dotnet build SAGAStructuralTools -c Release
      - uses: actions/upload-artifact@v4
        with:
          name: saga-structural-tools-build
          path: SAGAStructuralTools/bin/Release/**
```

Pontos importantes:

- **`runs-on: windows-latest` é obrigatório** — o projeto usa WPF (`UseWPF=true`) e `net48`, que só
  compilam em Windows. Não dá para usar runners Linux/macOS aqui.
- O job `test` roda **sem precisar de Revit instalado** — só testa `Core/Domain`/`Core/Mapping`
  isoladamente, uma vez resolvido o item 0.
- O job `build` compila os dois `TargetFrameworks` (`net48` + `net8.0-windows`) — falha aqui pega
  erros de compilação específicos de um dos dois targets antes de chegar em produção.
- **Proteção de branch**: depois que o pipeline existir, configurar no GitHub para exigir os checks
  `test` e `build` verdes antes de permitir merge em `main`.
- Job de **empacotamento/instalador** (seção 3) fica separado, disparado só por tag de release
  (`v*`), não em todo PR — build de instalador é mais lento e não precisa rodar a cada commit.

---

## 3. "Executável" — na prática, um instalador

Importante alinhar expectativa: o plugin em si **é uma DLL**, carregada pelo Revit via o manifesto
`.addin` — não existe (nem faz sentido existir) um `.exe` que "roda o SAGA Structural Tools"
standalone. O que faz sentido automatizar é um **instalador** que:

1. Detecta (ou pergunta) quais versões do Revit (2023 / 2026) estão instaladas na máquina do usuário.
2. Copia `SAGAStructuralTools.dll` (do target correto) + `SAGAStructuralTools.addin` +
   `Resources/Icons/*.png` para `%ProgramData%\Autodesk\Revit\Addins\2023` e/ou `...\2026`.
3. Idealmente também remove uma instalação anterior antes de copiar a nova (evitar DLL antiga presa
   por lock de processo, etc.).

Opções, da mais simples à mais "enterprise":

| Opção | Esforço | Quando faz sentido |
|---|---|---|
| **Zip + script PowerShell** (`install.ps1`) que o próprio usuário roda | Baixo — só empacota os artefatos do build + um script que faz os `Copy-Item` para as pastas certas | Uso interno, poucos usuários, você mesmo controla quem instala |
| **Inno Setup** | Médio — script declarativo (`.iss`), gera um `setup.exe` único com wizard, mas plenamente scriptável/gerável via CI | Bom equilíbrio: instalador "de verdade" (ícone, wizard, desinstalar pelo Painel de Controle) sem a complexidade do WiX |
| **WiX Toolset** | Alto — gera `.msi`, é o padrão usado por instaladores corporativos/Autodesk App Store | Só vale se algum dia for necessário MSI para deploy corporativo via GPO/SCCM, ou submissão à Autodesk App Store |

**Recomendação para agora:** Inno Setup. É o ponto de equilíbrio certo para o tamanho atual do
projeto — automatizável 100% em CI (o compilador `iscc.exe` roda via linha de comando), produz um
`.exe` único e profissional, e evolui facilmente para WiX depois se um dia for preciso.

O job de instalador entraria no pipeline assim (ilustrativo):

```yaml
  package:
    needs: build
    if: startsWith(github.ref, 'refs/tags/v')
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/download-artifact@v4
        with:
          name: saga-structural-tools-build
      - run: iscc installer/saga-installer.iss   # gera o .exe
      - uses: softprops/action-gh-release@v2      # anexa o instalador ao Release do GitHub
        with:
          files: installer/output/SAGAStructuralToolsSetup.exe
```

---

## 4. Versionamento

O histórico de commits já usa uma convenção informal (`feat: v0.7.0 — patamar intermediário`, etc.)
mas isso vive só na mensagem do commit, não em nenhum artefato rastreável. Sugestão:

- Adotar **tags git reais** (`git tag v0.7.0`) no commit que fecha cada versão, em vez de só citar
  a versão na mensagem — é o gatilho natural para o job de `package` da seção 3 e para criar um
  GitHub Release com changelog.
- Espelhar a mesma versão no `<Version>` do `.csproj` (hoje ausente — vale adicionar), para que a
  DLL final carregue a versão certa em metadados (`FileVersion`/`ProductVersion`), útil para
  suporte ("qual versão você tem instalada?").

---

## 5. Ordem sugerida de execução (quando decidirmos fazer)

1. Resolver o bloqueio da seção 0 (referências RevitAPI via NuGet) — desbloqueia tudo o resto.
2. Criar `SAGAStructuralTools.Tests` com os casos da seção 1.1 (maior retorno por esforço: pega
   bugs de validação de input com custo baixo).
3. Subir o workflow de CI da seção 2 (test + build) — já dá proteção de branch e feedback em PR.
4. Só depois, se fizer sentido pelo volume de releases, entrar em instalador (seção 3) e
   versionamento formal (seção 4).
5. RTF/testes dentro do Revit (seção 1.3, item 2) fica como investimento de médio prazo, não
   bloqueante para os itens acima.