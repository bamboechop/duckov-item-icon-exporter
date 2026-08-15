[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) { throw "Package directory not found: $PackageDirectory" }
$expected = @('DuckovItemIconExporter.dll', 'DuckovItemIconExporter.Core.dll', 'info.ini', 'preview.png')
$actual = @(Get-ChildItem -LiteralPath $PackageDirectory -File | Select-Object -ExpandProperty Name | Sort-Object)
if (($actual -join '|') -ne (($expected | Sort-Object) -join '|')) { throw "Package inventory is invalid. Expected only: $($expected -join ', '). Actual: $($actual -join ', ')" }
if (@(Get-ChildItem -LiteralPath $PackageDirectory -Recurse -File -Filter '*.dll').Count -ne 2) { throw 'Package contains an unexpected DLL.' }
if (@(Get-ChildItem -LiteralPath $PackageDirectory -Recurse -File | Where-Object { $_.Name -match '^(UnityEngine|ItemStatsSystem|TeamSoda\.Duckov\.Core).*\.dll$' }).Count -ne 0) { throw 'Game or Unity DLL leaked into the package.' }
$ini = Get-Content -LiteralPath (Join-Path $PackageDirectory 'info.ini') -Raw
foreach ($line in @('name = DuckovItemIconExporter', 'version = 1.0.0')) { if ($ini -notmatch [regex]::Escape($line)) { throw "info.ini is missing '$line'." } }
$preview = Get-Content -LiteralPath (Join-Path $PackageDirectory 'preview.png') -AsByteStream -TotalCount 8
$pngSignature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
if ($preview.Count -ne $pngSignature.Count) { throw 'preview.png does not have a valid PNG signature.' }
for ($index = 0; $index -lt $pngSignature.Count; $index++)
{
    if ($preview[$index] -ne $pngSignature[$index]) { throw 'preview.png does not have a valid PNG signature.' }
}
Write-Host "PASS: package contains only permitted exporter files: $($actual -join ', ')"
