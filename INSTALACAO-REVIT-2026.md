# Instalação no Revit 2026

## Gerar e instalar o pacote

1. Na branch `develop`, clone ou atualize o repositório.
2. Execute `powershell -ExecutionPolicy Bypass -File .\tools\New-Revit2026Package.ps1`.
3. Extraia `artifacts\packages\SAGAStructuralTools-Revit2026.zip`.
4. Feche o Revit.
5. Dê dois cliques em `INSTALAR.cmd`.
6. Abra o Revit 2026.

O instalador copia o add-in para o diretório do usuário, sem exigir acesso de
administrador:

```text
%APPDATA%\Autodesk\Revit\Addins\2026
```

Se houver outra cópia em
`C:\ProgramData\Autodesk\Revit\Addins\2026`, o instalador mostrará um aviso.
Remova a DLL antiga para evitar carregar versões diferentes por engano.

As famílias `.rfa` e os catálogos `.txt` não estão neste repositório. Copie a
biblioteca de famílias separadamente e, na primeira execução, selecione os novos
caminhos nas configurações do add-in.

## Pré-requisitos e compilação manual

Pré-requisitos:

- Autodesk Revit 2026 instalado no caminho padrão;
- .NET SDK 10 ou mais recente;
- Git.

```powershell
git clone https://github.com/Ulisses-Antonelli/Revit_SAGAStructuralTools.git
cd Revit_SAGAStructuralTools
git switch develop
dotnet build .\SAGAStructuralTools\SAGAStructuralTools.csproj `
  -c Release -f net10.0-windows -p:DeployToRevit=false
```
