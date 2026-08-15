[CmdletBinding()]
param(
    [Parameter()][string]$DuckovPath = $env:DUCKOV_PATH
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DuckovPath)) { throw 'Supply -DuckovPath or set DUCKOV_PATH to the Escape from Duckov installation.' }
$DuckovPath = [IO.Path]::GetFullPath($DuckovPath)
$managed = Join-Path $DuckovPath 'Duckov_Data\Managed'
foreach ($required in @('ItemStatsSystem.dll', 'TeamSoda.Duckov.Core.dll', 'TeamSoda.Duckov.Utilities.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.ImageConversionModule.dll', 'UnityEngine.IMGUIModule.dll', 'UnityEngine.UI.dll', 'UnityEngine.UIModule.dll', 'Unity.TextMeshPro.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $managed $required) -PathType Leaf)) { throw "Missing required game assembly: $required" }
}

& dotnet clean (Join-Path $repoRoot 'DuckovItemIconExporter.sln') --configuration Release --nologo "-p:DuckovPath=$DuckovPath"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet build (Join-Path $repoRoot 'DuckovItemIconExporter.sln') --configuration Release --nologo -warnaserror "-p:DuckovPath=$DuckovPath"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet run --project (Join-Path $repoRoot 'tests\DuckovItemIconExporter.Tests\DuckovItemIconExporter.Tests.csproj') --configuration Release --no-build --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet (Join-Path $repoRoot 'tools\DuckovItemIconExporter.ContractProbe\bin\Release\net8.0\DuckovItemIconExporter.ContractProbe.dll') $managed
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$packageRoot = Join-Path $repoRoot 'artifacts\package\DuckovItemIconExporter'
if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\DuckovItemIconExporter\bin\Release\netstandard2.1\DuckovItemIconExporter.dll') -Destination (Join-Path $packageRoot 'DuckovItemIconExporter.dll')
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\DuckovItemIconExporter\bin\Release\netstandard2.1\DuckovItemIconExporter.Core.dll') -Destination (Join-Path $packageRoot 'DuckovItemIconExporter.Core.dll')
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\DuckovItemIconExporter\info.ini') -Destination (Join-Path $packageRoot 'info.ini')
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageDirectory $packageRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$safeRepoRoot = $repoRoot.Replace('\', '/')
& git -c "safe.directory=$safeRepoRoot" -C $repoRoot check-ignore -q -- artifacts/package/DuckovItemIconExporter/DuckovItemIconExporter.dll
if ($LASTEXITCODE -ne 0) { throw 'Package output is not ignored by Git.' }
Write-Host "Build gate passed. Package: $packageRoot"
