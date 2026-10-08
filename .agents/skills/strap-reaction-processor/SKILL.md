---
name: strap-reaction-processor
description: Processar tabelas de reações de apoio do STRAP fornecidas como imagem, captura de tela, PDF, DOCX, RTF, TXT, XLSX ou tabela colada; consolidar os esforços por nó para bases de concreto e gerar uma planilha XLSX no padrão Import_Revit.
---

# Processador de Reações STRAP

Use esta habilidade quando o usuário fornecer resultados de reações de apoio do STRAP e pedir para transcrever, conferir, consolidar ou gerar uma planilha para Revit/Dynamo.

## Objetivo

Transformar os resultados brutos de cada nó/pilar em uma única linha final:

`NO_PILAR | X1 | X2 | X3_MAX | X3_MIN | X4 | X5 | X6`

Considere:

- X1 = FX
- X2 = FY
- X3 = FZ
- X4 = MX
- X5 = MY
- X6 = MZ

Esses esforços representam as reações de pilares metálicos transmitidas às bases de concreto.

Quando a solicitação envolver a criação ou manutenção do botão do Revit, leia
`references/revit-integration-spec.md` antes de propor a arquitetura ou alterar
o código. Mantenha o motor de processamento independente da API do Revit.

## Fontes aceitas

- imagem ou captura de tela;
- PDF textual ou digitalizado;
- DOCX;
- RTF;
- TXT;
- XLSX, XLS ou CSV;
- tabela colada na conversa.

Arquivos `.doc` antigos podem não permitir extração confiável. Quando isso ocorrer, solicite conversão para `.docx`, `.rtf` ou `.pdf`.

## Fluxo obrigatório

### 1. Identificar e ler a fonte

Determine o tipo de entrada antes de processar.

- Para imagens e PDFs digitalizados, leia visualmente a tabela. Verifique sinais negativos, separadores decimais, alinhamento das colunas e números dos nós.
- Para DOCX, RTF e TXT, extraia o texto e ignore cabeçalhos, endereços, datas, números de página, títulos repetidos e linhas vazias.
- Para planilhas, localize as colunas pelos cabeçalhos e pelo significado, não apenas pela posição.
- Quando houver uma fonte textual e uma imagem da mesma tabela, use a fonte textual como principal e a imagem para conferência.
- Não use valores de outros arquivos ou conversas para preencher lacunas do arquivo atual.

### 2. Montar os dados originais

Para cada nó, identifique exatamente:

- uma linha `Máx`, `Máximo` ou `Max`;
- uma linha `Mín`, `Mínimo` ou `Min`;
- seis esforços em cada linha;
- seis combinações em cada linha, quando existirem.

Normalize os nomes:

- `FX` e `X1` são equivalentes;
- `FY` e `X2` são equivalentes;
- `FZ` e `X3` são equivalentes;
- `MX` e `X4` são equivalentes;
- `MY` e `X5` são equivalentes;
- `MZ` e `X6` são equivalentes.

Preserve os valores brutos e as combinações na aba `Dados_Originais`.

### 3. Aplicar as regras estruturais

Para cada nó, compare as duas linhas da fonte.

#### Esforços horizontais e momentos

Calcule como valor positivo:

- `X1 = MAX(ABS(FX_1), ABS(FX_2))`
- `X2 = MAX(ABS(FY_1), ABS(FY_2))`
- `X4 = MAX(ABS(MX_1), ABS(MX_2))`
- `X5 = MAX(ABS(MY_1), ABS(MY_2))`
- `X6 = MAX(ABS(MZ_1), ABS(MZ_2))`

O sinal desses cinco resultados não deve ser preservado. O resultado final deve ser maior ou igual a zero.

#### Reação vertical

Compare numericamente os dois valores de FZ, preservando os sinais:

- `X3_MAX = MAX(FZ_1, FZ_2)`
- `X3_MIN = MIN(FZ_1, FZ_2)`

Nunca associe automaticamente `X3_MAX` à linha escrita `Máx`, nem `X3_MIN` à linha escrita `Mín`. O STRAP pode apresentar uma linha denominada Máx cujo FZ seja numericamente menor do que o FZ da linha denominada Mín.

Não classifique o sinal como compressão ou tração sem que a convenção de sinais do projeto tenha sido informada. Apenas preserve os sinais de FZ.

Leia `references/schema-and-rules.md` antes de calcular e use `references/test-cases.md` para conferir casos-limite.

### 4. Tratar rotação dos eixos

Aplique rotação somente quando o usuário informar que existe rotação de 90° entre os eixos do STRAP e os eixos usados no Revit:

- trocar X1 com X2;
- trocar X4 com X5;
- manter X3_MAX e X3_MIN;
- manter X6.

Quando a rotação não for informada:

- não aplique rotação;
- registre `Rotação não informada — nenhuma aplicada` na aba `Configuracao`;
- mencione isso na resposta final.

### 5. Tratar ambiguidades

Não adivinhe valores ilegíveis, cortados ou desalinhados.

Quando houver dúvida, informe:

- número do nó;
- linha Máx ou Mín;
- coluna;
- leituras possíveis.

Pare antes de gerar o arquivo final se a ambiguidade puder alterar um resultado.

Também pare e peça confirmação quando:

- faltar uma das duas linhas do nó;
- houver menos ou mais de seis valores;
- um mesmo nó aparecer repetido com dados conflitantes;
- as unidades mudarem dentro do documento;
- não for possível distinguir esforço de número de combinação.

### 6. Gerar o XLSX

Use `assets/REACOES_Import_Revit_MODELO.xlsx` como estrutura preferencial.

Preencha com valores numéricos, não com texto. Na aba `Import_Revit`, grave os resultados finais como valores calculados, não dependa de fórmulas externas ou recálculo do Excel.

Abas obrigatórias:

1. `Import_Revit`
2. `Dados_Originais`
3. `Combinacoes`, quando a fonte apresentar combinações
4. `Configuracao`
5. `Verificacao`

#### Import_Revit

Use exatamente as colunas:

`NO_PILAR | X1 | X2 | X3_MAX | X3_MIN | X4 | X5 | X6`

#### Dados_Originais

Registre cada linha da fonte com:

`NO_PILAR | CASO_ORIGEM | FX | FY | FZ | MX | MY | MZ | FX_COMB | FY_COMB | FZ_COMB | MX_COMB | MY_COMB | MZ_COMB | FONTE`

#### Combinacoes

Quando existirem combinações, use:

`NO_PILAR | X1_MAX_COMB | X1_MIN_COMB | X2_MAX_COMB | X2_MIN_COMB | X3_MAX_COMB | X3_MIN_COMB | X4_MAX_COMB | X4_MIN_COMB | X5_MAX_COMB | X5_MIN_COMB | X6_MAX_COMB | X6_MIN_COMB`

Nessa aba, `MAX_COMB` e `MIN_COMB` representam as linhas originais denominadas Máx e Mín no relatório, não necessariamente a ordem numérica final de FZ. Explique isso em `Configuracao`.

#### Configuracao

Registre:

- arquivo ou tipo de fonte;
- unidades encontradas;
- mapeamento X1 a X6;
- regra de módulo;
- regra de FZ;
- rotação;
- precisão decimal;
- observações.

#### Verificacao

Registre:

- quantidade de nós encontrados;
- quantidade de nós processados;
- nós incompletos;
- nós duplicados;
- valores ambíguos;
- confirmação de que X1, X2, X4, X5 e X6 são não negativos;
- confirmação de que X3_MAX é maior ou igual a X3_MIN em todos os nós;
- confirmação de que todos os sinais de FZ foram preservados;
- existência de combinações;
- status final `APROVADO` ou `REVISAR`.

## Precisão e unidades

- Não converta unidades sem solicitação.
- Registre as unidades exatamente como aparecem na fonte.
- Preserve todas as casas decimais relevantes.
- Não arredonde antes de comparar os valores.
- Use células numéricas no Excel.
- Use o ponto decimal internamente e formatação numérica adequada no arquivo.

## Verificações finais obrigatórias

Antes da entrega, valide:

1. Cada nó gerou exatamente uma linha em `Import_Revit`.
2. Cada nó possui dois valores de FZ.
3. X1, X2, X4, X5 e X6 são maiores ou iguais a zero.
4. X3_MAX é maior ou igual a X3_MIN.
5. Os sinais originais de FZ foram preservados.
6. Não existem nós omitidos ou duplicados sem justificativa.
7. Não existem erros de fórmula ou células com `#REF!`, `#VALUE!`, `#DIV/0!`, `#NAME?` ou `#N/A`.
8. O arquivo abre corretamente e contém todas as abas aplicáveis.
9. A aba `Verificacao` informa `APROVADO` somente quando não houver pendências.

## Resposta final

Entregue o arquivo XLSX e informe de forma curta:

- quantidade de nós processados;
- tipo de fonte;
- unidades;
- rotação aplicada ou não;
- existência de ambiguidades;
- resultado da verificação `X3_MAX >= X3_MIN`.

Não apresente uma tabela extensa no chat quando o arquivo já tiver sido criado.
