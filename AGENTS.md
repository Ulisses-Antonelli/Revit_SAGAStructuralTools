# SAGA Structural Tools

## Contexto

- Plugin para Autodesk Revit 2026.
- Linguagem principal: C#.
- Trabalhar de forma incremental e explicar as decisões ao usuário.
- Antes de alterar a solução, identificar os projetos, frameworks, padrões e
  convenções já existentes.
- Preservar alterações do usuário e evitar mudanças fora do escopo solicitado.

## Arquitetura

- Manter regras de domínio independentes da API do Revit.
- Separar leitura de arquivos, processamento estrutural, interface e escrita em
  parâmetros do Revit.
- Não colocar cálculos estruturais diretamente em comandos `IExternalCommand`
  ou em janelas.
- Encapsular operações que modificam o documento do Revit em transações
  pequenas e claramente nomeadas.
- Não abrir uma transação enquanto o usuário estiver escolhendo arquivos ou
  preenchendo a interface.

## Importação de reações STRAP

- Usar a skill `strap-reaction-processor` para tarefas relacionadas às reações.
- Seguir `references/schema-and-rules.md` e converter
  `references/test-cases.md` em testes automatizados.
- Para o comando do Revit, seguir
  `references/revit-integration-spec.md`.
- Não associar automaticamente `X3_MAX` à linha Máx do relatório.
- `X1`, `X2`, `X4`, `X5` e `X6` representam o maior módulo e devem ser
  não negativos.
- `X3_MAX` e `X3_MIN` preservam os sinais originais de FZ.
- A rotação de 90° troca `X1` com `X2` e `X4` com `X5`; `X3` e `X6`
  permanecem.
- Em bases agrupadas, somar as reações por combinação antes de calcular as
  envoltórias.
- Quando os apoios não estiverem no mesmo ponto, considerar
  `M_resultante = soma(M_i + r_i x F_i)`.
- Não adivinhar valores, unidades, combinações ou correspondências entre nós e
  elementos.

## Qualidade e verificação

- Criar testes unitários para o motor de processamento antes de conectá-lo ao
  Revit.
- Incluir casos positivos, negativos, sinais mistos, empate de módulo, rotação,
  agrupamento e transporte de momentos.
- Executar os testes e compilar os projetos afetados antes de concluir.
- Informar claramente qualquer teste que dependa de uma instalação local do
  Revit e não possa ser executado no ambiente atual.
- Considerar concluída uma alteração somente quando compilar, os testes
  independentes do Revit passarem e não houver erros conhecidos ocultados.

