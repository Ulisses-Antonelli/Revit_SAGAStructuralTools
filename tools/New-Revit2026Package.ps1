[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts\packages'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $repoRoot 'SAGAStructuralTools\SAGAStructuralTools.csproj'
$staging = Join-Path $repoRoot 'artifacts\Revit2026'

& dotnet build $project -c $Configuration -f net8.0-windows -m:1 `
    -p:DeployToRevit=false
if ($LASTEXITCODE -ne 0) {
    throw "A compilação falhou com o código $LASTEXITCODE."
}

& (Join-Path $PSScriptRoot 'Deploy-RevitPayload.ps1') `
    -StagingDirectory $staging `
    -ExpectedTargetFramework net8.0-windows `
    -ExpectedRevitVersion 2026 `
    -ValidateOnly

$packageRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'SAGA-Revit2026-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    $packagePayload = Join-Path $packageRoot 'payload'
    New-Item -ItemType Directory -Path $packagePayload -Force | Out-Null
    Get-ChildItem -LiteralPath $staging -Force |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName `
                -Destination $packagePayload -Recurse
        }
    foreach ($pair in @(
        @('Install-Revit2026.ps1', 'INSTALAR-Revit2026.ps1'),
        @('Uninstall-Revit2026.ps1', 'DESINSTALAR-Revit2026.ps1'),
        @('Deploy-RevitPayload.ps1', 'Deploy-RevitPayload.ps1'),
        @('Install-Revit2026.cmd', 'INSTALAR.cmd'),
        @('Uninstall-Revit2026.cmd', 'DESINSTALAR.cmd')
    )) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $pair[0]) `
            -Destination (Join-Path $packageRoot $pair[1])
    }
    @'
SAGA STRUCTURAL TOOLS — REVIT 2026

Feche o Revit e execute INSTALAR.cmd como administrador.
O payload validado será instalado em:
C:\ProgramData\Autodesk\Revit\Addins\2026

O instalador remove somente arquivos registrados no manifesto SAGA de uma
instalação anterior. Arquivos desconhecidos da pasta global não são removidos.
'@ | Set-Content -LiteralPath (Join-Path $packageRoot 'LEIA-ME.txt') `
        -Encoding UTF8

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $zip = Join-Path $OutputDirectory 'SAGAStructuralTools-Revit2026.zip'
    Compress-Archive -Path (Join-Path $packageRoot '*') `
        -DestinationPath $zip -Force
    Write-Output $zip
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolved = [IO.Path]::GetFullPath($packageRoot)
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolved)) {
        [IO.Directory]::Delete($resolved, $true)
    }
}
