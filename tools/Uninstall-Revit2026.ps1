[CmdletBinding()]
param(
    [switch]$RemoveUserData
)

$ErrorActionPreference = 'Stop'

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Feche o Revit antes de desinstalar o SAGA Structural Tools.'
}

$target = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026'
$payloadIcons = Join-Path $PSScriptRoot 'payload\Resources\Icons'

foreach ($name in @(
    'SAGAStructuralTools.dll',
    'SAGAStructuralTools.deps.json',
    'SAGAStructuralTools.addin'
)) {
    $path = Join-Path $target $name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

if (Test-Path -LiteralPath $payloadIcons) {
    Get-ChildItem -LiteralPath $payloadIcons -File -Filter '*.png' |
        ForEach-Object {
            $installedIcon = Join-Path $target ('Resources\Icons\' + $_.Name)
            if (Test-Path -LiteralPath $installedIcon) {
                Remove-Item -LiteralPath $installedIcon -Force
            }
        }
}

if ($RemoveUserData) {
    foreach ($name in @(
        'SAGAStructuralTools.settings',
        'SAGA_Debug.txt',
        'SAGA_RailLog.txt'
    )) {
        $path = Join-Path $target $name
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

$machineManifest = Join-Path $env:ProgramData `
    'Autodesk\Revit\Addins\2026\SAGAStructuralTools.addin'
if (Test-Path -LiteralPath $machineManifest) {
    Write-Warning (
        'Uma instalação antiga ainda existe em ProgramData e poderá voltar a ser carregada. ' +
        'Remova-a separadamente como administrador, se não quiser utilizá-la.'
    )
}

Write-Host 'Instalação por usuário removida.' -ForegroundColor Green
