[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputManifest,
    [Parameter(Mandatory = $true)][string]$ProjectReferenceManifest,
    [Parameter(Mandatory = $true)][string]$StagingDirectory,
    [Parameter(Mandatory = $true)][string]$ArtifactsRoot,
    [Parameter(Mandatory = $true)]
    [ValidateSet('net48', 'net8.0-windows')][string]$TargetFramework,
    [Parameter(Mandatory = $true)]
    [ValidateSet('2023', '2026')][string]$RevitVersion
)

$ErrorActionPreference = 'Stop'
$comparison = [StringComparison]::OrdinalIgnoreCase

function Get-NormalizedRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) {
        throw "Caminho de payload inválido: '$Path'."
    }
    $validationRoot = [IO.Path]::GetFullPath(
        (Join-Path ([IO.Path]::GetTempPath()) 'SAGA-Payload-Path-Validation'))
    $candidate = [IO.Path]::GetFullPath((Join-Path $validationRoot $Path))
    $prefix = $validationRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, $comparison)) {
        throw "O caminho relativo sai da pasta gerenciada pelo SAGA: '$Path'."
    }
    $relative = $candidate.Substring($prefix.Length).Replace('/', '\')
    if ([string]::IsNullOrWhiteSpace($relative)) {
        throw "Caminho vazio após normalização: '$Path'."
    }
    return $relative
}

if (($TargetFramework -eq 'net48' -and $RevitVersion -ne '2023') -or
    ($TargetFramework -eq 'net8.0-windows' -and $RevitVersion -ne '2026')) {
    throw "TargetFramework '$TargetFramework' não corresponde ao Revit $RevitVersion."
}

$artifacts = [IO.Path]::GetFullPath($ArtifactsRoot).TrimEnd('\', '/')
$staging = [IO.Path]::GetFullPath($StagingDirectory).TrimEnd('\', '/')
$expectedStaging = [IO.Path]::GetFullPath(
    (Join-Path $artifacts "Revit$RevitVersion")).TrimEnd('\', '/')
if (-not $staging.Equals($expectedStaging, $comparison)) {
    throw "Staging recusado. Esperado '$expectedStaging', recebido '$staging'."
}

$entries = [Collections.Generic.List[object]]::new()
$destinations = @{}
$fileNames = @{}
foreach ($line in Get-Content -LiteralPath $InputManifest -Encoding UTF8) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line.Split('|')
    if ($parts.Count -ne 3) {
        throw "Entrada inválida no inventário MSBuild: '$line'."
    }
    $kind = $parts[2]
    $source = [IO.Path]::GetFullPath($parts[0])
    $targetPath = $parts[1]
    if ($kind -eq 'Resource') {
        $targetPath = Join-Path 'SAGAStructuralTools\Resources\Icons' (
            [IO.Path]::GetFileName($source))
    }
    $relative = Get-NormalizedRelativePath $targetPath
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        if ($kind -eq 'OptionalSymbols') { continue }
        throw "Arquivo resolvido não encontrado: '$source'."
    }
    $leaf = [IO.Path]::GetFileName($relative)
    if ($leaf -ieq 'RevitAPI.dll' -or $leaf -ieq 'RevitAPIUI.dll') {
        throw "Assembly fornecida pelo Revit foi rejeitada: '$source'."
    }
    $destinationKey = $relative.ToUpperInvariant()
    if ($destinations.ContainsKey($destinationKey)) {
        $previous = $destinations[$destinationKey]
        if (-not $previous.Source.Equals($source, $comparison)) {
            throw "Colisão no destino '$relative': '$($previous.Source)' e '$source'."
        }
        continue
    }
    $fileKey = $leaf.ToUpperInvariant()
    if ($fileNames.ContainsKey($fileKey) -and
        -not $fileNames[$fileKey].Equals($source, $comparison)) {
        throw "Arquivos com o mesmo nome vieram de origens diferentes: '$leaf'."
    }
    $entry = [pscustomobject]@{
        Source = $source
        RelativePath = $relative
        Kind = $kind
    }
    $destinations[$destinationKey] = $entry
    $fileNames[$fileKey] = $source
    $entries.Add($entry)
}

$required = @(
    'SAGAStructuralTools.dll',
    'SAGAStructuralTools.addin',
    'SAGAStructuralTools.Strap.Domain.dll',
    'SAGAStructuralTools.Strap.Application.dll',
    'SAGAStructuralTools.Strap.Readers.dll',
    'DocumentFormat.OpenXml.dll',
    'DocumentFormat.OpenXml.Framework.dll',
    'System.Text.Encoding.CodePages.dll',
    'UglyToad.PdfPig.dll',
    'UglyToad.PdfPig.Core.dll',
    'UglyToad.PdfPig.DocumentLayoutAnalysis.dll',
    'UglyToad.PdfPig.Fonts.dll',
    'UglyToad.PdfPig.Package.dll',
    'UglyToad.PdfPig.Tokenization.dll',
    'UglyToad.PdfPig.Tokens.dll'
)
if ($TargetFramework -eq 'net8.0-windows') {
    $required += @(
        'SAGAStructuralTools.deps.json',
        'System.IO.Packaging.dll'
    )
}
foreach ($name in $required) {
    if (-not $fileNames.ContainsKey($name.ToUpperInvariant())) {
        throw "Arquivo obrigatório ausente do payload resolvido: '$name'."
    }
}
foreach ($projectAssembly in
    (Get-Content -LiteralPath $ProjectReferenceManifest -Encoding UTF8 |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
    if (-not $fileNames.ContainsKey($projectAssembly.Trim().ToUpperInvariant())) {
        throw "ProjectReference ausente do payload: '$projectAssembly'."
    }
}
if (-not ($entries | Where-Object {
    $_.RelativePath -like 'SAGAStructuralTools\Resources\Icons\*.png'
})) {
    throw 'Nenhum recurso obrigatório SAGAStructuralTools\Resources\Icons\*.png foi resolvido.'
}

$temp = Join-Path $artifacts (
    ".Revit$RevitVersion.staging-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    $manifestFiles = [Collections.Generic.List[object]]::new()
    foreach ($entry in ($entries | Sort-Object RelativePath)) {
        $destination = [IO.Path]::GetFullPath(
            (Join-Path $temp $entry.RelativePath))
        $tempPrefix = $temp.TrimEnd('\', '/') +
            [IO.Path]::DirectorySeparatorChar
        if (-not $destination.StartsWith($tempPrefix, $comparison)) {
            throw "Destino saiu do staging temporário: '$($entry.RelativePath)'."
        }
        $parent = Split-Path -Parent $destination
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Copy-Item -LiteralPath $entry.Source -Destination $destination
        $manifestFiles.Add([ordered]@{
            path = $entry.RelativePath.Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
            kind = $entry.Kind
        })
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        product = 'SAGAStructuralTools'
        targetFramework = $TargetFramework
        revitVersion = $RevitVersion
        files = $manifestFiles
    }
    $manifestPath = Join-Path $temp (
        'SAGAStructuralTools\SAGAStructuralTools.payload.json')
    $manifest | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $manifestPath -Encoding UTF8

    & (Join-Path $PSScriptRoot 'Deploy-RevitPayload.ps1') `
        -StagingDirectory $temp `
        -ExpectedTargetFramework $TargetFramework `
        -ExpectedRevitVersion $RevitVersion `
        -ValidateOnly

    if (Test-Path -LiteralPath $staging) {
        [IO.Directory]::Delete($staging, $true)
    }
    [IO.Directory]::Move($temp, $staging)
    Write-Host "Payload validado: $staging"
}
finally {
    if (Test-Path -LiteralPath $temp) {
        [IO.Directory]::Delete($temp, $true)
    }
}
