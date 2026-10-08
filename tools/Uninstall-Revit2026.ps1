[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Feche o Revit antes de desinstalar o SAGA Structural Tools.'
}

$target = Join-Path $env:ProgramData 'Autodesk\Revit\Addins\2026'
$privateDirectory = Join-Path $target 'SAGAStructuralTools'
$manifestPath = Join-Path $privateDirectory 'SAGAStructuralTools.payload.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Write-Host 'Nenhum manifesto SAGA instalado foi encontrado; nada foi removido.'
    return
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or
    $manifest.product -ne 'SAGAStructuralTools') {
    throw 'O manifesto instalado não pertence a um formato SAGA reconhecido.'
}
$root = [IO.Path]::GetFullPath($target).TrimEnd('\', '/')
$prefix = $root + [IO.Path]::DirectorySeparatorChar
foreach ($file in $manifest.files) {
    $relative = ([string]$file.path).Replace('/', '\')
    if ([string]::IsNullOrWhiteSpace($relative) -or
        [IO.Path]::IsPathRooted($relative)) {
        throw "Caminho inválido no manifesto instalado: '$relative'."
    }
    if ($relative -ne 'SAGAStructuralTools.addin' -and
        -not $relative.StartsWith(
            'SAGAStructuralTools\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Caminho fora do layout SAGA permitido: '$relative'."
    }
    $resolved = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $resolved.StartsWith(
        $prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "O caminho sai da pasta gerenciada: '$relative'."
    }
    if (Test-Path -LiteralPath $resolved -PathType Leaf) {
        Remove-Item -LiteralPath $resolved -Force
    }
}
Remove-Item -LiteralPath $manifestPath -Force
if ((Test-Path -LiteralPath $privateDirectory -PathType Container) -and
    -not (Get-ChildItem -LiteralPath $privateDirectory -Force |
        Select-Object -First 1)) {
    Remove-Item -LiteralPath $privateDirectory
}
Write-Host 'Arquivos registrados pelo manifesto SAGA foram removidos.' `
    -ForegroundColor Green
