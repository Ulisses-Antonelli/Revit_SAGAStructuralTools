# Instalação no Revit 2026

## Opção mais simples: pacote ZIP

1. Baixe o ZIP anexado à versão de trabalho no GitHub.
2. Extraia todos os arquivos.
3. Feche o Revit.
4. Dê dois cliques em `INSTALAR.cmd`.
5. Abra o Revit 2026.

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

## Continuar o desenvolvimento em outro computador

Pré-requisitos:

- Autodesk Revit 2026 instalado no caminho padrão;
- .NET SDK 8 ou mais recente;
- Git.

```powershell
git clone https://github.com/Ulisses-Antonelli/Revit_SAGAStructuralTools.git
cd Revit_SAGAStructuralTools
git switch feature/rail-transitions
dotnet build .\SAGAStructuralTools\SAGAStructuralTools.csproj `
  -c Release -f net8.0-windows -p:DeployToRevit=false
```

Para gerar novamente o ZIP:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-Revit2026Package.ps1
```
