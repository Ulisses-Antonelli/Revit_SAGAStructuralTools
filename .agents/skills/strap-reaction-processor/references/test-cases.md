# Casos de teste obrigatórios

## Caso 1 — tração e compressão em FZ

Entrada:

```text
FX: 0.136 e -2.056
FY: 0.375 e 0.239
FZ: 3.143 e -3.432
MX: 0.083 e 0.073
MY: -0.104 e -0.781
MZ: -0.004 e 0.381
```

Saída:

```text
X1 = 2.056
X2 = 0.375
X3_MAX = 3.143
X3_MIN = -3.432
X4 = 0.083
X5 = 0.781
X6 = 0.381
```

## Caso 2 — linha Mín contém o maior FZ

Entrada:

```text
FZ da linha Máx = 1.596
FZ da linha Mín = 7.112
```

Saída:

```text
X3_MAX = 7.112
X3_MIN = 1.596
```

Esse caso deve impedir a associação automática entre rótulo da linha e ordem numérica.

## Caso 3 — ambos os FZ positivos

Entrada:

```text
FZ = 20.678 e 16.306
```

Saída:

```text
X3_MAX = 20.678
X3_MIN = 16.306
```

## Caso 4 — ambos os FZ negativos

Entrada:

```text
FZ = -2.100 e -8.400
```

Saída:

```text
X3_MAX = -2.100
X3_MIN = -8.400
```

## Caso 5 — maior módulo é negativo na fonte

Entrada:

```text
MY = 1.184 e -4.139
```

Saída:

```text
X5 = 4.139
```

## Caso 6 — empate de módulo

Entrada:

```text
FX = 0.015 e -0.015
```

Saída:

```text
X1 = 0.015
```

O resultado consolidado continua positivo.

## Caso 7 — rotação de 90°

Antes da rotação:

```text
X1 = 2.000
X2 = 5.000
X4 = 0.300
X5 = 1.200
X6 = 0.100
```

Depois:

```text
X1 = 5.000
X2 = 2.000
X4 = 1.200
X5 = 0.300
X6 = 0.100
```
