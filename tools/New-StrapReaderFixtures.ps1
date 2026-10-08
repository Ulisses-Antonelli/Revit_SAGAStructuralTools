param(
    [string]$OutputDirectory = (
        Join-Path $PSScriptRoot '..\SAGAStructuralTools.Strap.Readers.Tests\Fixtures')
)

$ErrorActionPreference = 'Stop'
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($output) | Out-Null

$lines = @(
    'Unidades: kN',
    'No 101',
    "FX`tFY`tFZ`tMX`tMY`tMZ",
    "Max`t1`t2`t3`t4`t5`t6",
    "Min`t-2`t-4`t-6`t-8`t-10`t-12"
)

[System.IO.File]::WriteAllBytes(
    (Join-Path $output 'empty.txt'),
    [byte[]]::new(0))

$windows1252 = [System.Text.Encoding]::GetEncoding(1252)
[System.IO.File]::WriteAllBytes(
    (Join-Path $output 'reactions-windows1252.txt'),
    $windows1252.GetBytes(($lines -replace '^Max', 'Máx' -replace '^Min', 'Mín') -join "`r`n"))

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Escape-Xml([string]$value) {
    return [System.Security.SecurityElement]::Escape($value)
}

function New-Docx {
    param(
        [string]$Path,
        [bool]$UseTable
    )

    $temporary = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid())
    [System.IO.Directory]::CreateDirectory((Join-Path $temporary '_rels')) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $temporary 'word')) | Out-Null

    [System.IO.File]::WriteAllText(
        (Join-Path $temporary '[Content_Types].xml'),
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
        '<Default Extension="xml" ContentType="application/xml"/>' +
        '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>' +
        '</Types>')
    [System.IO.File]::WriteAllText(
        (Join-Path $temporary '_rels\.rels'),
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>' +
        '</Relationships>')

    if ($UseTable) {
        $rows = foreach ($line in $lines) {
            $cells = foreach ($cell in ($line -split "`t")) {
                '<w:tc><w:p><w:r><w:t xml:space="preserve">' +
                (Escape-Xml $cell) +
                '</w:t></w:r></w:p></w:tc>'
            }
            '<w:tr>' + ($cells -join '') + '</w:tr>'
        }
        $body = '<w:tbl>' + ($rows -join '') + '</w:tbl>'
    }
    else {
        $paragraphs = foreach ($line in $lines) {
            $parts = $line -split "`t"
            $runs = for ($index = 0; $index -lt $parts.Count; $index++) {
                if ($index -gt 0) { '<w:r><w:tab/></w:r>' }
                '<w:r><w:t xml:space="preserve">' +
                (Escape-Xml $parts[$index]) +
                '</w:t></w:r>'
            }
            '<w:p>' + ($runs -join '') + '</w:p>'
        }
        $body = $paragraphs -join ''
    }

    [System.IO.File]::WriteAllText(
        (Join-Path $temporary 'word\document.xml'),
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">' +
        '<w:body>' + $body + '<w:sectPr/></w:body></w:document>')

    if (Test-Path -LiteralPath $Path) {
        [System.IO.File]::Delete($Path)
    }
    $archiveStream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite)
    $archive = [System.IO.Compression.ZipArchive]::new(
        $archiveStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)
    foreach ($entryName in @(
        '[Content_Types].xml',
        '_rels/.rels',
        'word/document.xml'
    )) {
        $sourcePath = Join-Path $temporary ($entryName.Replace('/', '\'))
        $entry = $archive.CreateEntry(
            $entryName,
            [System.IO.Compression.CompressionLevel]::Optimal)
        $entryStream = $entry.Open()
        $sourceStream = [System.IO.File]::OpenRead($sourcePath)
        $sourceStream.CopyTo($entryStream)
        $sourceStream.Dispose()
        $entryStream.Dispose()
    }
    $archive.Dispose()
    $archiveStream.Dispose()
    [System.IO.Directory]::Delete($temporary, $true)
}

function New-Pdf {
    param(
        [string]$Path,
        [string[][]]$Pages
    )

    $objects = [System.Collections.Generic.List[string]]::new()
    $pageIds = [System.Collections.Generic.List[int]]::new()
    $fontId = 3 + ($Pages.Count * 2)

    for ($pageIndex = 0; $pageIndex -lt $Pages.Count; $pageIndex++) {
        $pageId = 3 + ($pageIndex * 2)
        $contentId = $pageId + 1
        $pageIds.Add($pageId)
        $contentLines = [System.Collections.Generic.List[string]]::new()
        $contentLines.Add('BT /F1 10 Tf 50 790 Td')
        foreach ($line in $Pages[$pageIndex]) {
            $escaped = $line.Replace('\', '\\').Replace('(', '\(').Replace(')', '\)')
            $contentLines.Add("($escaped) Tj 0 -18 Td")
        }
        $contentLines.Add('ET')
        $content = $contentLines -join "`n"
        $objects.Add("$pageId 0 obj`n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 $fontId 0 R >> >> /Contents $contentId 0 R >>`nendobj")
        $objects.Add("$contentId 0 obj`n<< /Length $([System.Text.Encoding]::ASCII.GetByteCount($content)) >>`nstream`n$content`nendstream`nendobj")
    }

    $kids = ($pageIds | ForEach-Object { "$_ 0 R" }) -join ' '
    $all = [System.Collections.Generic.List[string]]::new()
    $all.Add('1 0 obj' + "`n<< /Type /Catalog /Pages 2 0 R >>`nendobj")
    $all.Add("2 0 obj`n<< /Type /Pages /Kids [$kids] /Count $($Pages.Count) >>`nendobj")
    foreach ($item in $objects) { $all.Add($item) }
    $all.Add("$fontId 0 obj`n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>`nendobj")

    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.StreamWriter]::new($stream, [System.Text.Encoding]::ASCII, 1024, $true)
    $writer.NewLine = "`n"
    $writer.Write("%PDF-1.4`n")
    $writer.Flush()
    $offsets = [System.Collections.Generic.List[int]]::new()
    $offsets.Add(0)
    foreach ($object in $all) {
        $offsets.Add([int]$stream.Position)
        $writer.Write($object + "`n")
        $writer.Flush()
    }
    $xref = [int]$stream.Position
    $writer.Write("xref`n0 $($all.Count + 1)`n")
    $writer.Write("0000000000 65535 f `n")
    for ($index = 1; $index -le $all.Count; $index++) {
        $writer.Write(('{0:0000000000} 00000 n ' -f $offsets[$index]) + "`n")
    }
    $writer.Write("trailer`n<< /Size $($all.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF")
    $writer.Flush()
    [System.IO.File]::WriteAllBytes($Path, $stream.ToArray())
    $writer.Dispose()
    $stream.Dispose()
}

New-Docx (Join-Path $output 'reactions-table.docx') $true
New-Docx (Join-Path $output 'reactions-tabulated.docx') $false

New-Pdf (Join-Path $output 'reactions-single-page.pdf') @(,$lines)
New-Pdf (Join-Path $output 'reactions-multi-page.pdf') @(
    $lines[0..3],
    @($lines[4], 'Rodape anonimo', 'Fim do relatorio')
)
New-Pdf (Join-Path $output 'reactions-no-text.pdf') @(@())
New-Pdf (Join-Path $output 'reactions-insufficient-text.pdf') @(@('Pouco texto'))
New-Pdf (Join-Path $output 'reactions-unstructured.pdf') @(
    @('um dois tres quatro cinco seis sete oito nove dez onze doze')
)
