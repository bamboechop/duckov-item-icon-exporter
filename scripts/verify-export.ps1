[CmdletBinding()]
param([Parameter(Mandatory)][string]$ExportDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ExportDirectory = [IO.Path]::GetFullPath($ExportDirectory)
foreach ($file in @('items.json', 'items.csv', 'index.html', 'summary.txt')) { if (-not (Test-Path -LiteralPath (Join-Path $ExportDirectory $file) -PathType Leaf)) { throw "Missing export manifest: $file" } }
$icons = Join-Path $ExportDirectory 'icons'
if (-not (Test-Path -LiteralPath $icons -PathType Container)) { throw 'Missing icons directory.' }
$items = @(Get-Content -LiteralPath (Join-Path $ExportDirectory 'items.json') -Raw | ConvertFrom-Json)
if ($items.Count -eq 0) { throw 'Manifest is empty.' }
$ids = @($items | ForEach-Object { [int]$_.typeId })
if (($ids | Select-Object -Unique).Count -ne $ids.Count) { throw 'TypeID is not unique in JSON manifest.' }
if (($ids -join ',') -ne (($ids | Sort-Object) -join ',')) { throw 'JSON manifest is not TypeID ordered.' }
$csvRows = @(Get-Content -LiteralPath (Join-Path $ExportDirectory 'items.csv'))
if ($csvRows.Count -ne $items.Count + 1) { throw 'CSV row count does not agree with JSON.' }
$csvIds = @($csvRows | Select-Object -Skip 1 | ForEach-Object {
    if ($_ -notmatch '^"(-?\d+)",') { throw "CSV row has no valid quoted TypeID: $_" }
    [int]$Matches[1]
})
if (($csvIds -join ',') -ne ($ids -join ',')) { throw 'CSV TypeID order does not agree with JSON.' }
$successful = @($items | Where-Object { $_.status -in @('Exported', 'NativeFallbackExported') })
$unavailable = @($items | Where-Object { $_.status -eq 'NoIconAvailable' })
$failed = @($items | Where-Object { $_.status -eq 'Failed' })
if (@(Get-ChildItem -LiteralPath $icons -Filter '*.png' -File).Count -ne $successful.Count) { throw 'Actual PNG count does not agree with successful manifest rows.' }
foreach ($item in $items) {
    if ([IO.Path]::GetFileName([string]$item.outputPng) -ne [string]$item.outputPng) { throw "Unsafe output filename for TypeID $($item.typeId)." }
    if ($item.status -in @('Exported', 'NativeFallbackExported')) {
        $path = Join-Path $icons $item.outputPng
        $bytes = [IO.File]::ReadAllBytes($path)
        if ($bytes.Length -lt 24 -or (($bytes[0..7] -join ',') -ne '137,80,78,71,13,10,26,10')) { throw "Invalid PNG signature: $path" }
        $width = [BitConverter]::ToInt32(@($bytes[19],$bytes[18],$bytes[17],$bytes[16]), 0)
        $height = [BitConverter]::ToInt32(@($bytes[23],$bytes[22],$bytes[21],$bytes[20]), 0)
        if ($width -le 0 -or $height -le 0) { throw "PNG has non-positive dimensions: $path" }
    }
    elseif ([string]::IsNullOrWhiteSpace([string]$item.reason)) { throw "Non-exported TypeID $($item.typeId) has no explicit reason." }
}
if (-not (Get-Content -LiteralPath (Join-Path $ExportDirectory 'index.html') -Raw).Contains('<tbody>')) { throw 'Gallery HTML is incomplete.' }
Write-Host "PASS: export verified. Discovered=$($items.Count), successful=$($successful.Count), unavailable=$($unavailable.Count), failed=$($failed.Count)"
