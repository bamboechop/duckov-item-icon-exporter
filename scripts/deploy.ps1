[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()][string]$DuckovPath = $env:DUCKOV_PATH,
    [switch]$Deploy,
    [switch]$ReplaceExisting
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DuckovPath)) { throw 'Supply -DuckovPath or set DUCKOV_PATH.' }
$DuckovPath = [IO.Path]::GetFullPath($DuckovPath)
$package = Join-Path $repoRoot 'artifacts\package\DuckovItemIconExporter'
$target = Join-Path $DuckovPath 'Duckov_Data\Mods\DuckovItemIconExporter'
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageDirectory $package

Write-Host 'Deployment inventory:'
Get-ChildItem -LiteralPath $package -File | Select-Object Name, Length
Write-Host "Only deployment target: $target"
if (Test-Path -LiteralPath $target) {
    $currentFiles = @(Get-ChildItem -LiteralPath $target -File | Select-Object -ExpandProperty Name | Sort-Object)
    $expectedFiles = @('DuckovItemIconExporter.Core.dll', 'DuckovItemIconExporter.dll', 'info.ini', 'preview.png')
    $legacyFiles = @('DuckovItemIconExporter.Core.dll', 'DuckovItemIconExporter.dll', 'info.ini')
    $isCurrentInventory = ($currentFiles -join '|') -eq ($expectedFiles -join '|')
    $isLegacyInventory = ($currentFiles -join '|') -eq ($legacyFiles -join '|')
    if (-not ($isCurrentInventory -or $isLegacyInventory)) { throw "Existing target has an unexpected inventory; refusing to alter it: $($currentFiles -join ', ')" }
    if (-not $ReplaceExisting) { throw "Deployment target already exists: $target. Do not replace or back it up without explicit approval." }
    if ($isLegacyInventory) { Write-Host 'Existing target has the known pre-preview exporter inventory; preview.png will be added in place.' }
    else { Write-Host 'Existing target contains only the current exporter files and will be overwritten in place; no backup or deletion will occur.' }
}
else { Write-Host 'Target does not exist. No files were written.' }
if (-not $Deploy) { return }
if ($PSCmdlet.ShouldProcess($target, 'Deploy Duckov Item Icon Exporter')) {
    if (-not (Test-Path -LiteralPath $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
    Copy-Item -Path (Join-Path $package '*') -Destination $target -Force
    Write-Host "Deployed only to: $target"
}
