#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string]$GameManagedPath,
    [Parameter(Mandatory = $true)][string]$BepInExCorePath
)
$ErrorActionPreference = 'Stop'
$GameManagedPath = (Resolve-Path -LiteralPath $GameManagedPath).Path
$BepInExCorePath = (Resolve-Path -LiteralPath $BepInExCorePath).Path
$projectRoot = $PSScriptRoot
function Run([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE" }
}
Run 'dotnet' @('build', (Join-Path $projectRoot 'src/PeakSaveManager/PeakSaveManager.csproj'), '-c', 'Release',
    "-p:GameManagedPath=$GameManagedPath", "-p:BepInExCorePath=$BepInExCorePath")
# Reflection tests run in fresh processes to avoid assembly identity collisions.
Run 'pwsh' @('-NoProfile', '-File', (Join-Path $projectRoot 'tools/Verify-ItemUsageData.ps1'),
    '-GameManagedPath', $GameManagedPath, '-BepInExCorePath', $BepInExCorePath)
Run 'pwsh' @('-NoProfile', '-File', (Join-Path $projectRoot 'tools/Verify-Compatibility.ps1'),
    '-GameManagedPath', $GameManagedPath, '-BepInExCorePath', $BepInExCorePath)
Run 'pwsh' @('-NoProfile', '-File', (Join-Path $projectRoot 'tools/Verify-PlayerRestoration.ps1'),
    '-GameManagedPath', $GameManagedPath, '-BepInExCorePath', $BepInExCorePath)
Run 'dotnet' @('run', '--project', (Join-Path $projectRoot 'tools/SaveVerificationTests/SaveVerificationTests.csproj'),
    '-c', 'Release', "-p:GameManagedPath=$GameManagedPath")
Write-Output 'Build and managed verification passed. Unity/Photon gameplay was not tested.'
