[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$project = Join-Path $repoRoot 'SAGAStructuralTools\SAGAStructuralTools.csproj'
$manifest = Join-Path $repoRoot 'SAGAStructuralTools\SAGAStructuralTools.addin'
$icons = Join-Path $repoRoot 'SAGAStructuralTools\Resources\Icons'
$buildOutput = Join-Path $repoRoot `
    ('SAGAStructuralTools\bin\' + $Configuration + '\net8.0-windows')

& dotnet build $project `
    -c $Configuration `
    -f net8.0-windows `
    -m:1 `
    -p:DeployToRevit=false
if ($LASTEXITCODE -ne 0) {
    throw "A compilação falhou com o código $LASTEXITCODE."
}

$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('SAGA-Revit2026-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $stagingRoot 'payload'
$payloadIcons = Join-Path $payload 'Resources\Icons'
New-Item -ItemType Directory -Path $payloadIcons -Force | Out-Null

try {
    Copy-Item -LiteralPath (Join-Path $buildOutput 'SAGAStructuralTools.dll') `
        -Destination $payload -Force

    $deps = Join-Path $buildOutput 'SAGAStructuralTools.deps.json'
    if (Test-Path -LiteralPath $deps) {
        Copy-Item -LiteralPath $deps -Destination $payload -Force
    }

    Copy-Item -LiteralPath $manifest -Destination $payload -Force
    Get-ChildItem -LiteralPath $icons -File -Filter '*.png' |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $payloadIcons -Force
        }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Revit2026.ps1') `
        -Destination (Join-Path $stagingRoot 'INSTALAR-Revit2026.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-Revit2026.ps1') `
        -Destination (Join-Path $stagingRoot 'DESINSTALAR-Revit2026.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Revit2026.cmd') `
        -Destination (Join-Path $stagingRoot 'INSTALAR.cmd') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-Revit2026.cmd') `
        -Destination (Join-Path $stagingRoot 'DESINSTALAR.cmd') -Force

    @'
SAGA STRUCTURAL TOOLS — REVIT 2026

INSTALAR
1. Extraia todo o ZIP.
2. Feche o Revit.
3. Dê dois cliques em INSTALAR.cmd.
4. Abra o Revit 2026.

O add-in será instalado somente para o usuário atual em:
%APPDATA%\Autodesk\Revit\Addins\2026

Como alternativa, abra o PowerShell nesta pasta e execute:
powershell -ExecutionPolicy Bypass -File .\INSTALAR-Revit2026.ps1

As famílias .rfa e seus catálogos .txt não fazem parte deste pacote e devem
ser copiados separadamente para uma pasta estável no computador.
'@ | Set-Content -LiteralPath (Join-Path $stagingRoot 'LEIA-ME.txt') `
        -Encoding UTF8

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $zip = Join-Path $OutputDirectory `
        'SAGAStructuralTools-Revit2026-feature-rail-transitions.zip'
    Compress-Archive -Path (Join-Path $stagingRoot '*') `
        -DestinationPath $zip -Force
    Write-Output $zip
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedStaging = [IO.Path]::GetFullPath($stagingRoot)
    if ($resolvedStaging.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStaging)) {
        [IO.Directory]::Delete($resolvedStaging, $true)
    }
}
