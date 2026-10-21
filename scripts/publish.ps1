#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string]$TokenFile,
    [string]$TcliPath = 'tcli'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'manifest.json') -Raw | ConvertFrom-Json
$package = Join-Path $projectRoot "dist/$($manifest.name)-$($manifest.version_number).zip"
$info = Get-Content -LiteralPath (Join-Path $projectRoot 'dist/package-info.json') -Raw | ConvertFrom-Json
if (!(Test-Path -LiteralPath $package) -or (Get-FileHash -LiteralPath $package).Hash -ne $info.PackageSHA256 -or
    $info.Version -ne $manifest.version_number -or $info.Name -ne $manifest.name) { throw 'Build and verify the current package first.' }
try {
    $existing = Invoke-RestMethod -Uri "https://thunderstore.io/api/experimental/package/LeonYew/$($manifest.name)/"
    if ([version]$existing.latest.version_number -ge [version]$manifest.version_number) { throw 'This version or a newer release already exists; do not upload it again.' }
}
catch {
    if (!$_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw }
}
$token = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $TokenFile).Path).Trim()
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Token file is empty.' }
$userToken = [Environment]::GetEnvironmentVariable('TCLI_AUTH_TOKEN', 'User')
if (![string]::IsNullOrWhiteSpace($userToken) -and $userToken -ne $token) {
    throw 'A different user-level TCLI_AUTH_TOKEN is configured; inspect that credential before publishing.'
}
$previousToken = [Environment]::GetEnvironmentVariable('TCLI_AUTH_TOKEN', 'Process')
$previousRollForward = [Environment]::GetEnvironmentVariable('DOTNET_ROLL_FORWARD', 'Process')
Push-Location $projectRoot
try {
    $env:TCLI_AUTH_TOKEN = $token
    $env:DOTNET_ROLL_FORWARD = 'Major'
    & $TcliPath publish --config-path (Join-Path $projectRoot 'thunderstore.toml') --file $package
    if ($LASTEXITCODE -ne 0) { throw "Thunderstore publishing failed with exit code $LASTEXITCODE" }
}
finally {
    [Environment]::SetEnvironmentVariable('TCLI_AUTH_TOKEN', $previousToken, 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_ROLL_FORWARD', $previousRollForward, 'Process')
    $token = $null
    $userToken = $null
    Pop-Location
}
