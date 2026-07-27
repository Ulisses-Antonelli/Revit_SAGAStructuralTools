# Especificação de integração com o Revit

## Escopo inicial

Criar o comando `Importar Reações STRAP` para o plugin SAGA Structural Tools.

Aceitar inicialmente:

- DOCX;
- RTF;
- TXT;
- PDF com texto selecionável.

Não executar OCR no plugin. Quando um PDF não possuir texto estruturado,
orientar o usuário a processar a imagem externamente e importar o XLSX
resultante.

## Arquitetura

Separar obrigatoriamente:

1. leitores de arquivos;
2. modelo de dados brutos;
3. motor de consolidação estrutural;
4. agrupamento de nós por base;
5. interface de conferência;
6. integração com parâmetros e elementos do Revit.

O motor de consolidação e seus testes não devem depender da API do Revit.

## Fluxo individual

Para cada nó:

1. ler as linhas Máx e Mín;
2. validar seis esforços e suas combinações, quando disponíveis;
3. aplicar as regras de `references/schema-and-rules.md`;
4. aplicar a rotação somente quando marcada;
5. exibir os valores calculados antes de gravá-los no Revit.

A opção de rotação deve iniciar desmarcada. Quando ativada:

- trocar X1 com X2;
- trocar X4 com X5;
- manter X3_MAX, X3_MIN e X6.

## Bases com vários nós

Permitir agrupar dois ou mais nós em uma única base.

Não somar diretamente envoltórias provenientes de combinações diferentes como
se fossem simultâneas. Para o cálculo rigoroso:

1. solicitar as reações completas por combinação somente para os nós agrupados;
2. permitir digitação manual e colagem de várias linhas copiadas do STRAP;
3. validar que todos os nós do grupo possuem as mesmas combinações;
4. aplicar a rotação individual de cada nó antes da soma;
5. somar por combinação;
6. calcular a envoltória somente depois da soma.

Disponibilizar dois métodos:

### Soma direta

Usar quando todas as reações já estiverem referidas ao mesmo ponto:

```text
F_resultante = soma(F_i)
M_resultante = soma(M_i)
```

### Resultante transportada

Usar quando os apoios estiverem em posições diferentes. Transportar cada reação
ao ponto de referência da base:

```text
F_resultante = soma(F_i)
M_resultante = soma(M_i + r_i x F_i)
```

Permitir obter as coordenadas dos elementos pelo Revit e permitir que o usuário
confira ou substitua manualmente o ponto de referência.

Registrar no relatório:

- nós que formam cada grupo;
- método utilizado;
- ponto de referência;
- rotações individuais;
- combinações ausentes;
- valores inseridos manualmente;
- resultado da verificação.

## Interface mínima

Prever:

- seleção do arquivo;
- identificação do tipo e das unidades;
- lista de nós encontrados;
- mapeamento entre número do nó e elemento do Revit;
- opção de rotação de 90°;
- criação e edição de grupos;
- entrada ou colagem das reações por combinação;
- escolha entre soma direta e resultante transportada;
- prévia dos valores finais;
- relatório de nós processados, agrupados, não encontrados ou inconsistentes.

## Testes mínimos

Converter todos os casos de `references/test-cases.md` em testes automatizados.
Adicionar testes para:

- rotação antes da soma;
- soma por combinação;
- combinações ausentes em um dos nós;
- dois apoios com forças opostas;
- transporte de força horizontal;
- transporte de força vertical;
- ponto de referência coincidente com o apoio;
- grupo com dois ou mais nós;
- bloqueio da soma rigorosa quando existirem apenas envoltórias;
- preservação dos sinais de FZ após agrupamento.
