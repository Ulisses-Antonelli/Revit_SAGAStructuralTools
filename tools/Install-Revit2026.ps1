[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Feche o Revit antes de instalar ou atualizar o SAGA Structural Tools.'
}

$payload = Join-Path $PSScriptRoot 'payload'
if (-not (Test-Path -LiteralPath (Join-Path $payload 'SAGAStructuralTools.dll'))) {
    throw 'O pacote está incompleto: payload\SAGAStructuralTools.dll não foi encontrado.'
}

$target = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026'
$targetIcons = Join-Path $target 'Resources\Icons'
New-Item -ItemType Directory -Path $targetIcons -Force | Out-Null

foreach ($name in @(
    'SAGAStructuralTools.dll',
    'SAGAStructuralTools.deps.json',
    'SAGAStructuralTools.addin'
)) {
    $source = Join-Path $payload $name
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $target $name) -Force
    }
}

$sourceIcons = Join-Path $payload 'Resources\Icons'
if (Test-Path -LiteralPath $sourceIcons) {
    Get-ChildItem -LiteralPath $sourceIcons -File -Filter '*.png' |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $targetIcons -Force
        }
}

Get-ChildItem -LiteralPath $target -Recurse -File |
    Where-Object {
        $_.Name -like 'SAGAStructuralTools*' -or
        $_.FullName.StartsWith($targetIcons, [StringComparison]::OrdinalIgnoreCase)
    } |
    Unblock-File -ErrorAction SilentlyContinue

$machineManifest = Join-Path $env:ProgramData `
    'Autodesk\Revit\Addins\2026\SAGAStructuralTools.addin'
if (Test-Path -LiteralPath $machineManifest) {
    Write-Warning (
        'Também existe uma instalação em ProgramData. A versão instalada para este ' +
        'usuário terá prioridade, mas remova a cópia antiga de ProgramData para evitar dúvidas.'
    )
}

Write-Host ''
Write-Host 'SAGA Structural Tools instalado para o Revit 2026 em:' -ForegroundColor Green
Write-Host $target
Write-Host 'Agora você pode abrir o Revit 2026.'
