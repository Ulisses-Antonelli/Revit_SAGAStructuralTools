[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Feche o Revit antes de instalar ou atualizar o SAGA Structural Tools.'
}

$payload = Join-Path $PSScriptRoot 'payload'
$target = Join-Path $env:ProgramData 'Autodesk\Revit\Addins\2026'
& (Join-Path $PSScriptRoot 'Deploy-RevitPayload.ps1') `
    -StagingDirectory $payload `
    -DestinationDirectory $target `
    -ExpectedTargetFramework net8.0-windows `
    -ExpectedRevitVersion 2026

Write-Host 'SAGA Structural Tools instalado para o Revit 2026.' `
    -ForegroundColor Green
