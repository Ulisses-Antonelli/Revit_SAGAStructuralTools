# Geometria do Guarda-Corpo — Regras de Cálculo (API Revit)

Especificação da lógica de posicionamento de **montantes**, **corrimão** e **travessas**.
Todas as cotas internas em pés (Revit); a UI trabalha em mm (`/304.8`).

## Convenções de eixos

- `lineStart`, `lineEnd`: extremidades da linha de perímetro selecionada.
- `dir = (lineEnd − lineStart).Normalize()`: direção ao longo do caminho.
- `lateral = (−dir.Y, dir.X, 0)`: perpendicular horizontal ao caminho.
- `L`: comprimento da linha (mm).
- `H`: altura do guarda-corpo (`HandrailHeight`, mm) — cota do **eixo do corrimão pela referência da família**.

---

## 1. Montantes — distribuição ao longo do caminho

A API insere elementos pelo **eixo central**. Para a face externa do 1º e do último
montante coincidir com as pontas da linha, recua-se cada extremo em `W/2`:

```
W       = largura da seção do montante (SectionSize.SectionWidthMm)
L_eixos = L − W
eixo[i] = W/2 + PostOffset[i] · L_eixos / L      (remapeia [0, L] → [W/2, L − W/2])
```

- 1º montante: eixo em `W/2` → face externa em `0` (início da linha).
- Último montante: eixo em `L − W/2` → face externa em `L` (fim da linha).
- Intermediários: distribuídos dentro de `L_eixos`.

As posições finais ficam em `RailSegment.AxisOffsets` (consumidas pelo `InfillBuilder`).

> Alternativa equivalente (só vigas): justificação Y = Centro + Z Topo/Centro/Inferior no
> 1º/intermediários/último. **Não usada** porque colunas (`Pilares estruturais`) não expõem
> justificação Z, e combiná-la com o recuo `W/2` corrigiria em dobro.

---

## 2. Montantes — restrição de altura (não vazar pelo corrimão)

O topo do montante para no **eixo central** da seção do corrimão:

```
R      = meia-altura da seção do corrimão (SectionSize.SectionHeightMm / 2)
limitZ = lineStart.Z + (H − R)                 // eixo do corrimão
topZ   = min(limitZ + PostTopOffset, limitZ)   // PostTopOffset só recua para baixo
baseZ  = lineStart.Z − PostBaseOffset          // nasce na linha selecionada
```

### Colunas (OST_StructuralColumns)
Colunas são controladas por **Nível base/topo + deslocamentos**, não por Z absoluto.
Por padrão o Revit ancora o topo no **próximo nível acima** → coluna vai de nível a nível.
Correção (`SetColumnExtents`): forçar **Nível superior = Nível base** e definir:

```
FAMILY_TOP_LEVEL_PARAM        = level.Id
FAMILY_BASE_LEVEL_OFFSET_PARAM = baseZ − level.Elevation
FAMILY_TOP_LEVEL_OFFSET_PARAM  = topZ  − level.Elevation
```

### Vigas (OST_StructuralFraming)
Linha vertical explícita `Line.CreateBound(basePt, topPt)` — Z exato, sem jogo de níveis.

---

## 3. Corrimão

- Linha de eixo em `lineStart.Z + H`, offset lateral `HandrailAxisOffset`.
- Justificação: **YZ = Uniforme, Y = Centro, Z = Topo** (perfil pendura abaixo do eixo).

---

## 4. Travessas horizontais

- Extremos exatamente nos **eixos** do 1º e último montante → usam `seg.AxisOffsets`
  (não a face do montante, não as pontas da linha).
- Justificação: **Y = Centro, Z = Centro** (perfil centrado na linha de eixo).
- Distribuição equidistante: entre a base e o **EIXO** do corrimão (não o topo):

```
railAxisZ = lineStart.Z + H − R
z[i]      = baseZ + (railAxisZ − baseZ) · (i+1) / (nBarras + 1)
```

- Distribuição manual: `z = baseZ + BarConfig.Distance`.

---

## 5. Fechamento em quadro

- Topo/base de cada vão entre montantes consecutivos (usa `seg.AxisOffsets`).
- Cantoneira fechada: + 1 vertical por montante (evita duplicar arestas compartilhadas).

---

## Dependência crítica: leitura de dimensões (`SectionSize`)

`W` (montante) e `R` (corrimão) são lidos por **nome de parâmetro** da família
(`bf`, `b`, `D`, `Diâmetro`, `d`, `h`, ...). Se o nome não bater, retorna `0` e o
algoritmo **degrada com segurança** (sem recuo / topo no eixo nominal `H`).

Diagnóstico: a linha `PostBuilder: ... W=..mm | railR=..mm | topZ=..mm` em
`SAGA_RailLog.txt` mostra o que foi lido. `W=0` ou `railR=0` ⇒ nome de parâmetro não
reconhecido — adicionar à lista em `SectionSize`, ou medir pela bounding box da instância.

---

## Trabalho futuro — estabilidade sob rotação (perfil assimétrico)

**Status: adiado** (só necessário quando se usar perfil assimétrico, ex.: cantoneira, com
giro/flip de aba). Para tubo simétrico não há problema — Centro = tudo.

**Problema:** justificação nativa Y/Z é relativa ao sistema local da instância; giro de 180°
em perfil assimétrico inverte esse sistema (Top↔Bottom, Esq↔Dir), bagunçando a posição.

**Já resiliente hoje:** offsets laterais (vetor global), recuo W/2 (`MoveElement`), topo do
montante (coordenada Z medida), rotação da coluna (`RotateElement`), travessas (Centro —
estável sob flip). O único ponto com justificação instável é o **corrimão** (Z=Topo, Y=usuário).

**Plano acordado quando for implementar (escopo = só o corrimão):**
1. Trocar `Z=Topo` por `Z=Centro` + posicionar a linha de eixo em `HandrailHeight − R`
   (mover globalmente por `MoveElement`), replicando o "topo" sem justificação.
2. Trocar `Y=justificativa` por `Y=Centro` + mover lateralmente `±W/2` para Esquerda/Direita.
3. Flip de aba (futuro) via `RotateElement` global, nunca por justificação.
4. **Medição de seção projetada na direção da linha** (não BoundingBox alinhada ao mundo),
   para ser exata também em tramos diagonais.
