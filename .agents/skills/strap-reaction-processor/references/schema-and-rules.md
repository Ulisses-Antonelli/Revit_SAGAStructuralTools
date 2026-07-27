# Esquema e regras de consolidação

## Significado estrutural

Cada nó representa um pilar metálico e suas reações na base de concreto.

| Saída | Origem | Regra final |
|---|---|---|
| X1 | FX | maior módulo, positivo |
| X2 | FY | maior módulo, positivo |
| X3_MAX | FZ | maior valor numérico, com sinal |
| X3_MIN | FZ | menor valor numérico, com sinal |
| X4 | MX | maior módulo, positivo |
| X5 | MY | maior módulo, positivo |
| X6 | MZ | maior módulo, positivo |

## Fórmulas

Para dois casos por nó:

```text
X1 = max(abs(FX_a), abs(FX_b))
X2 = max(abs(FY_a), abs(FY_b))
X3_MAX = max(FZ_a, FZ_b)
X3_MIN = min(FZ_a, FZ_b)
X4 = max(abs(MX_a), abs(MX_b))
X5 = max(abs(MY_a), abs(MY_b))
X6 = max(abs(MZ_a), abs(MZ_b))
```

## Regras de sinais

- X1, X2, X4, X5 e X6 sempre são entregues como valores positivos ou zero.
- X3_MAX e X3_MIN preservam os sinais lidos.
- Não aplicar valor absoluto em FZ.
- Não inferir qual sinal representa compressão ou tração sem a convenção do projeto.

## Ordem das linhas do STRAP

As palavras `Máx` e `Mín` são rótulos do relatório e não garantem que o valor de FZ da linha `Máx` seja numericamente maior que o da linha `Mín`.

Por isso:

```text
FZ da linha Máx = 1.596
FZ da linha Mín = 7.112

X3_MAX = 7.112
X3_MIN = 1.596
```

## Rotação de 90°

Somente quando solicitada:

```text
X1_novo = X2_original
X2_novo = X1_original
X4_novo = X5_original
X5_novo = X4_original
X3_MAX, X3_MIN e X6 permanecem.
```

A rotação é aplicada depois da consolidação por módulo.
