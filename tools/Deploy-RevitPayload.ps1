[CmdletBinding(DefaultParameterSetName = 'Deploy')]
param(
    [Parameter(Mandatory = $true)][string]$StagingDirectory,
    [Parameter(Mandatory = $true, ParameterSetName = 'Deploy')]
    [string]$DestinationDirectory,
    [Parameter(Mandatory = $true)]
    [ValidateSet('net48', 'net8.0-windows')][string]$ExpectedTargetFramework,
    [Parameter(Mandatory = $true)]
    [ValidateSet('2023', '2026')][string]$ExpectedRevitVersion,
    [Parameter(ParameterSetName = 'Validate')][switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$privateDirectoryName = 'SAGAStructuralTools'
$addinName = 'SAGAStructuralTools.addin'
$manifestRelativePath =
    'SAGAStructuralTools\SAGAStructuralTools.payload.json'
$comparison = [StringComparison]::OrdinalIgnoreCase

function Resolve-ManagedPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath)) {
        throw "Caminho relativo inválido no manifesto: '$RelativePath'."
    }
    $normalizedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $resolved = [IO.Path]::GetFullPath(
        (Join-Path $normalizedRoot $RelativePath))
    $prefix = $normalizedRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, $comparison)) {
        throw "O caminho do manifesto sai da pasta gerenciada: '$RelativePath'."
    }
    return $resolved
}

function Get-AssemblyTargetFramework {
    param([Parameter(Mandatory = $true)][string]$AssemblyPath)

    $content = [Text.Encoding]::ASCII.GetString(
        [IO.File]::ReadAllBytes($AssemblyPath))
    $match = [regex]::Match(
        $content,
        '\.NET(?:Framework|CoreApp|Standard),Version=v[0-9.]+')
    if (-not $match.Success) {
        throw "TargetFrameworkAttribute não encontrado em '$AssemblyPath'."
    }
    return $match.Value
}

function Read-AndValidatePayload {
    param([Parameter(Mandatory = $true)][string]$Root)

    $rootPath = [IO.Path]::GetFullPath($Root)
    $manifestPath = Join-Path $rootPath $manifestRelativePath
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Manifesto de payload ausente: '$manifestPath'."
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or
        $manifest.product -ne 'SAGAStructuralTools') {
        throw "Manifesto de payload inválido em '$manifestPath'."
    }
    if ($manifest.targetFramework -ne $ExpectedTargetFramework -or
        [string]$manifest.revitVersion -ne $ExpectedRevitVersion) {
        throw "Payload incompatível: $($manifest.targetFramework)/Revit $($manifest.revitVersion)."
    }

    $rootFiles = @(Get-ChildItem -LiteralPath $rootPath -File)
    if ($rootFiles.Count -ne 1 -or $rootFiles[0].Name -ne $addinName) {
        $found = ($rootFiles | ForEach-Object Name) -join ', '
        throw "A raiz do payload deve conter somente '$addinName'. Encontrado: '$found'."
    }
    $rootDirectories = @(Get-ChildItem -LiteralPath $rootPath -Directory)
    if ($rootDirectories.Count -ne 1 -or
        $rootDirectories[0].Name -ne $privateDirectoryName) {
        $found = ($rootDirectories | ForEach-Object Name) -join ', '
        throw "A raiz do payload deve conter somente a pasta '$privateDirectoryName'. Encontrado: '$found'."
    }

    $paths = @{}
    $names = @{}
    foreach ($file in $manifest.files) {
        $relative = ([string]$file.path).Replace('/', '\')
        if ($relative -ne $addinName -and
            -not $relative.StartsWith(
                "$privateDirectoryName\",
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Arquivo fora do layout público/privado permitido: '$relative'."
        }
        $resolved = Resolve-ManagedPath $rootPath $relative
        $key = $relative.ToUpperInvariant()
        if ($paths.ContainsKey($key)) {
            throw "Caminho duplicado no manifesto: '$relative'."
        }
        $leaf = [IO.Path]::GetFileName($relative)
        if ($leaf -ieq 'RevitAPI.dll' -or $leaf -ieq 'RevitAPIUI.dll') {
            throw "Assembly fornecida pelo Revit foi rejeitada: '$relative'."
        }
        $nameKey = $leaf.ToUpperInvariant()
        if ($names.ContainsKey($nameKey) -and $names[$nameKey] -ne $key) {
            throw "Nome de arquivo duplicado em destinos diferentes: '$leaf'."
        }
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "Arquivo declarado não existe no staging: '$relative'."
        }
        if ((Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash -ne
            [string]$file.sha256) {
            throw "Hash divergente no staging: '$relative'."
        }
        $paths[$key] = [pscustomobject]@{
            RelativePath = $relative
            SourcePath = $resolved
        }
        $names[$nameKey] = $key
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
    if ($ExpectedTargetFramework -eq 'net8.0-windows') {
        $required += @(
            'SAGAStructuralTools.deps.json',
            'System.IO.Packaging.dll'
        )
    }
    foreach ($name in $required) {
        if (-not $names.ContainsKey($name.ToUpperInvariant())) {
            throw "Arquivo obrigatório ausente: '$name'."
        }
    }
    $addinPath = Join-Path $rootPath $addinName
    $addinXml = [xml](Get-Content -LiteralPath $addinPath -Raw -Encoding UTF8)
    $assemblyValue = [string]$addinXml.RevitAddIns.AddIn.Assembly
    $expectedAssembly =
        '.\SAGAStructuralTools\SAGAStructuralTools.dll'
    if ($assemblyValue -ne $expectedAssembly) {
        throw "Assembly inválida no .addin: '$assemblyValue'. Esperado '$expectedAssembly'."
    }
    $root = $addinXml.DocumentElement
    $allSettings = @($addinXml.SelectNodes('//ManifestSettings'))
    $directSettings = @($addinXml.SelectNodes(
        '/RevitAddIns/ManifestSettings'))
    $nestedSettings = @($addinXml.SelectNodes(
        '/RevitAddIns/AddIn/ManifestSettings'))
    if ($ExpectedTargetFramework -eq 'net48') {
        if ($allSettings.Count -ne 0) {
            throw 'O manifesto Revit 2023/net48 não pode conter ManifestSettings.'
        }
    }
    else {
        if ($allSettings.Count -ne 1 -or
            $directSettings.Count -ne 1 -or
            $nestedSettings.Count -ne 0) {
            throw "O manifesto Revit 2026 deve conter exatamente um RevitAddIns/ManifestSettings e nenhum ManifestSettings dentro de AddIn ou em outro nível. Encontrado: total=$($allSettings.Count), direto=$($directSettings.Count), dentroDeAddIn=$($nestedSettings.Count)."
        }
        $settings = $directSettings[0]
        $useContextNodes = @($settings.ChildNodes | Where-Object {
            $_.NodeType -eq [Xml.XmlNodeType]::Element -and
            $_.Name -eq 'UseRevitContext'
        })
        $contextNameNodes = @($settings.ChildNodes | Where-Object {
            $_.NodeType -eq [Xml.XmlNodeType]::Element -and
            $_.Name -eq 'ContextName'
        })
        if ($useContextNodes.Count -ne 1 -or
            $contextNameNodes.Count -ne 1 -or
            [string]$useContextNodes[0].InnerText -cne 'False' -or
            [string]$contextNameNodes[0].InnerText -cne 'SAGAStructuralTools') {
            throw 'O ManifestSettings do Revit 2026 deve conter exatamente UseRevitContext=False e ContextName=SAGAStructuralTools.'
        }
    }
    $expectedAssemblyFramework =
        if ($ExpectedTargetFramework -eq 'net48') {
            '.NETFramework,Version=v4.8'
        }
        else {
            '.NETCoreApp,Version=v8.0'
        }
    foreach ($name in @(
        'SAGAStructuralTools.dll',
        'SAGAStructuralTools.Strap.Readers.dll'
    )) {
        $entry = $paths[$names[$name.ToUpperInvariant()]]
        $actualFramework = Get-AssemblyTargetFramework $entry.SourcePath
        if ($actualFramework -ne $expectedAssemblyFramework) {
            throw "Assembly do target incorreto: '$name' usa '$actualFramework', esperado '$expectedAssemblyFramework'."
        }
    }
    if (-not ($paths.Values | Where-Object {
        $_.RelativePath -like 'SAGAStructuralTools\Resources\Icons\*.png'
    })) {
        throw 'Recursos obrigatórios SAGAStructuralTools\Resources\Icons\*.png ausentes.'
    }
    foreach ($actual in
        (Get-ChildItem -LiteralPath $rootPath -Recurse -File |
            Where-Object { $_.FullName -ne $manifestPath })) {
        $relative = $actual.FullName.Substring(
            $rootPath.TrimEnd('\', '/').Length + 1)
        if (-not $paths.ContainsKey($relative.ToUpperInvariant())) {
            throw "Arquivo não declarado encontrado no staging: '$relative'."
        }
    }
    return [pscustomobject]@{
        Root = $rootPath
        ManifestPath = $manifestPath
        Files = @($paths.Values)
    }
}

$validated = Read-AndValidatePayload $StagingDirectory
if ($ValidateOnly) {
    Write-Host "Validação do payload aprovada: $($validated.Root)"
    return
}

$destinationRoot = [IO.Path]::GetFullPath($DestinationDirectory)
$newPaths = @{}
foreach ($file in $validated.Files) {
    $newPaths[$file.RelativePath.ToUpperInvariant()] =
        Resolve-ManagedPath $destinationRoot $file.RelativePath
}
$installedManifestPath = Join-Path $destinationRoot $manifestRelativePath
$oldPaths = @{}
if (Test-Path -LiteralPath $installedManifestPath -PathType Leaf) {
    $oldManifest = Get-Content -LiteralPath $installedManifestPath -Raw `
        -Encoding UTF8 | ConvertFrom-Json
    if ($oldManifest.schemaVersion -ne 1 -or
        $oldManifest.product -ne 'SAGAStructuralTools') {
        throw 'O manifesto instalado não pertence a um formato SAGA reconhecido.'
    }
    foreach ($oldFile in $oldManifest.files) {
        $relative = ([string]$oldFile.path).Replace('/', '\')
        if ($relative -ne $addinName -and
            -not $relative.StartsWith(
                "$privateDirectoryName\",
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "O manifesto instalado contém caminho fora do layout SAGA: '$relative'."
        }
        $oldPaths[$relative.ToUpperInvariant()] =
            Resolve-ManagedPath $destinationRoot $relative
    }
}

# Nenhuma escrita ocorre antes da validação integral e das colisões acima.
if (-not (Test-Path -LiteralPath $destinationRoot)) {
    New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
}
$privateFiles = @($validated.Files | Where-Object {
    $_.RelativePath.StartsWith(
        "$privateDirectoryName\",
        [StringComparison]::OrdinalIgnoreCase)
})
$addinFiles = @($validated.Files | Where-Object {
    $_.RelativePath -eq $addinName
})
if ($addinFiles.Count -ne 1) {
    throw "O payload deve conter exatamente um '$addinName'."
}

# Primeiro materializa integralmente a subpasta privada.
foreach ($file in $privateFiles) {
    $destination = $newPaths[$file.RelativePath.ToUpperInvariant()]
    $parent = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Copy-Item -LiteralPath $file.SourcePath -Destination $destination -Force
}
foreach ($key in $oldPaths.Keys) {
    if (-not $newPaths.ContainsKey($key) -and
        (Test-Path -LiteralPath $oldPaths[$key] -PathType Leaf)) {
        Remove-Item -LiteralPath $oldPaths[$key] -Force
    }
}

# O manifesto privado é atualizado somente após as DLLs e recursos.
$manifestParent = Split-Path -Parent $installedManifestPath
if (-not (Test-Path -LiteralPath $manifestParent)) {
    New-Item -ItemType Directory -Path $manifestParent -Force | Out-Null
}
Copy-Item -LiteralPath $validated.ManifestPath `
    -Destination $installedManifestPath -Force

# O .addin público é deliberadamente a última escrita do deploy.
$addinSource = $addinFiles[0].SourcePath
$addinDestination = Join-Path $destinationRoot $addinName
Copy-Item -LiteralPath $addinSource -Destination $addinDestination -Force
Write-Host "SAGA Structural Tools instalado em: $destinationRoot"
