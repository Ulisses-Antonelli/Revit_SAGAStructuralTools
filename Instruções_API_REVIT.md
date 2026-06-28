
# Descrição do Aplicativo (Especificação do MVP)

## 1. Visão Geral

O **Conversor IFC para Famílias Gerdau** é um plugin nativo para o **Autodesk Revit 2023** projetado para automatizar a conversão de perfis metálicos genéricos (provenientes de arquivos IFC vinculados ou importados) em famílias estruturais nativas do catálogo da Gerdau.

O objetivo principal é eliminar o retrabalho manual de modelagem, garantir a precisão geométrica e viabilizar a extração exata de quantitativos e memórias de cálculo no padrão brasileiro.

---

## 2. Casos de Uso Principais (Use Cases)

### UC01: Configuração do Ambiente (Setup)

* **Atores:** Projetista de Estruturas / Desenvolvedor.
* **Fluxo Principal:** O usuário acessa a aba de configurações do plugin e aponta para o diretório local onde estão armazenadas as famílias nativas da Gerdau (`.rfa`). O plugin indexa os perfis disponíveis (ex: W, HP, Cantoneiras) para uso posterior.

### UC02: Conversão de Perfis IFC

* **Atores:** Projetista de Estruturas.
* **Fluxo Principal:**
1. O usuário abre o modelo Revit contendo o IFC importado/vinculado.
2. Clica no botão "Importar/Converter IFC" no Ribbon (icone e botão criados por nós).
3. Seleciona o arquivo IFC original ou os elementos desejados na tela.
4. O sistema lê as propriedades geométricas e de texto das entidades (`IfcBeam`, `IfcColumn` e outros caso existam ( mais especificamente para estruturas metálicas, porem acho que um IFC vindo do strap só vigas, cantoneiras laminadas, perfis U laminados e provavelmente perfis Dobrados )).
5. O motor limpa as strings de nomenclatura e faz o *match* (de-para) com o catálogo Gerdau indexado.
6. As famílias nativas são inseridas nas coordenadas exatas, vetores de orientação e rotação dos elementos originais.
7. O elemento IFC original é ocultado ou deletado.



---

## 3. Matriz de Funcionalidades (Features do MVP)

| ID | Feature | Descrição |
| --- | --- | --- |
| **FT01** | **Interface de Usuário Integrada** | Criação de uma Tab dedicada no Ribbon do Revit ("Gerdau Tools") com ícone e botão de acesso via `IExternalApplication`. |
| **FT02** | **Janela Unificada (WPF)** | Janela única em WPF (evitando pop-ups excessivos) contendo campos para: caminho das famílias locais, seleção do IFC e progresso. |
| **FT03** | **Motor de Mapeamento (De-Para)** | Algoritmo de limpeza de strings para associar nomes do IFC (ex: `W200X22_5`) ao padrão Gerdau (`W 200 x 22.5`). |
| **FT04** | **Substituição Geométrica Nativa** | Clonagem de coordenadas, pontos de inserção (`XYZ`) e rotação dos perfis para garantir o alinhamento da estrutura. |
| **FT05** | **Painel de Log e Exceções** | Área de texto em tempo real (ReadOnly) que exibe o relatório final com sucessos e falhas geradas durante a conversão. |

---

## 4. Fluxos Alternativos e Tratamento de Exceções

### Fluxo Alternativo: Mapeamento por Aproximação (Dicionário Flexível)

Se o nome do perfil no IFC não for idêntico ao da Gerdau por questões de caracteres especiais (traços, sublinhados, espaços), o plugin aplica uma expressão regular (Regex) para tentar encontrar a correspondência exata por dimensões de alma e mesa antes de descartar o elemento.

### Exceção: Perfil Não Encontrado no Catálogo

* **Cenário:** O IFC traz um perfil soldado customizado ou de outro fabricante que não consta nas famílias Gerdau configuradas.
* **Comportamento:** O plugin **não interrompe** o processo. O elemento específico é pulado, sua ID e nome original são armazenados e injetados na *Janela de Erros/Log* ao final do processo, mantendo o IFC original visível para que o projetista faça o ajuste manual.

### Exceção: Falha de Orientação/Geometria Variável

* **Cenário:** Perfis com inclinações complexas ou seções variáveis.
* **Comportamento:** O sistema tenta aplicar a linha de localização (`LocationLine`). Caso a geometria falhe na inserção da nova família nativa, o erro é capturado pelo bloco `try-catch`, a transação faz o *Rollback* apenas daquele elemento falho, e o processo continua para os demais perfis.

---

## 5. Restrições Técnicas de Desenvolvimento (VS Code)

* **Ambiente de Execução:** Revit 2023 (`.NET Framework 4.8` / C# clássico).
* **IDE de Desenvolvimento:** VS Code (utilizando o formato SDK moderno do MSBuild no `.csproj`).
* **Configuração de Compilação:** Referências `RevitAPI.dll` e `RevitAPIUI.dll` setadas estritamente com `Private=False` (`Copy Local = False`) para evitar duplicação de bibliotecas e travamentos de memória no ciclo de execução do Revit.

---

Este documento serve como a "bússola" do seu MVP.

Arquitetura sugerida MVVM, com separação das pastas e responsabilidades, o que acha?

eu não sei que nome seria mais adequado para esta ferramenta, mas poderiamos nomear assim como utilizar algum icone ja criado ou fazer um em svg( não sei se para plugin essa extenção é aceita)