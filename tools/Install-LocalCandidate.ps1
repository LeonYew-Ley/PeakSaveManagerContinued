#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string]$ProfileRoot,
    [Parameter(Mandatory = $true)][string]$GameRoot
)
$ErrorActionPreference = 'Stop'
if (Get-Process PEAK -ErrorAction SilentlyContinue) { throw 'Exit PEAK before installing this candidate.' }
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$ProfileRoot = (Resolve-Path -LiteralPath $ProfileRoot).Path
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$pluginsRoot = (Resolve-Path -LiteralPath (Join-Path $ProfileRoot 'BepInEx/plugins')).Path
$targetRoot = [IO.Path]::GetFullPath((Join-Path $pluginsRoot 'LeonYew-The_All_You_Need_Save_Manager_Continued'))
if (!$targetRoot.StartsWith($pluginsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Installation target is outside the selected profile plugins directory.'
}
if (!(Test-Path -LiteralPath $targetRoot -PathType Container)) { throw 'The existing continued save-manager package was not found.' }
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.version_number
$dllRelative = 'PeakSaveManagerContinued/PeakSaveManagerContinued.dll'
$dllSource = Join-Path $projectRoot 'build/PeakSaveManagerContinued.dll'
if ($version -notmatch '^\d+\.\d+\.\d+$' -or
    [Reflection.AssemblyName]::GetAssemblyName($dllSource).Version.ToString() -ne "$version.0" -or
    [Diagnostics.FileVersionInfo]::GetVersionInfo($dllSource).FileVersion -ne "$version.0") {
    throw 'Candidate DLL and package version do not agree.'
}
$oldDll = Join-Path $targetRoot $dllRelative
$oldVersion = [Reflection.AssemblyName]::GetAssemblyName($oldDll).Version.ToString()
$installRoot = Join-Path $projectRoot ('.tools/local-install/basic-{0}-{1}-{2}' -f $version, (Get-Date -Format 'yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N').Substring(0, 8))
$backupRoot = Join-Path $installRoot 'backup'
$stageRoot = Join-Path $installRoot 'stage'
New-Item -ItemType Directory -Path $backupRoot, $stageRoot | Out-Null

$protected = @{}
foreach ($saveRoot in @((Join-Path $GameRoot 'PeakSaves'), (Join-Path $env:USERPROFILE 'AppData/LocalLow/LandCrab/PEAK/PeakSaves'))) {
    if (Test-Path -LiteralPath $saveRoot -PathType Container) {
        Get-ChildItem -LiteralPath $saveRoot -File -Recurse | ForEach-Object { $protected[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName).Hash }
    }
}
$config = Join-Path $ProfileRoot 'BepInEx/config/com.lucasandersen.peakallyouneedsavemanager.cfg'
if (Test-Path -LiteralPath $config -PathType Leaf) { $protected[$config] = (Get-FileHash -LiteralPath $config).Hash }
$records = @()
foreach ($relative in @('manifest.json', 'README.md', 'CHANGELOG.md', 'NOTICE.md', 'icon.png', $dllRelative)) {
    $source = if ($relative -eq $dllRelative) { $dllSource } else { Join-Path $projectRoot $relative }
    $destination = [IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
    if (!$destination.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'File target is outside the selected plugin package.'
    }
    $backup = Join-Path $backupRoot $relative
    $stage = Join-Path $stageRoot $relative
    if (!(Test-Path -LiteralPath $destination -PathType Leaf)) { throw "Installed file missing: $relative" }
    New-Item -ItemType Directory -Path (Split-Path $backup), (Split-Path $stage) -Force | Out-Null
    Copy-Item -LiteralPath $destination -Destination $backup
    Copy-Item -LiteralPath $source -Destination $stage
    $oldHash = (Get-FileHash -LiteralPath $destination).Hash
    $newHash = (Get-FileHash -LiteralPath $source).Hash
    if ((Get-FileHash -LiteralPath $backup).Hash -ne $oldHash -or (Get-FileHash -LiteralPath $stage).Hash -ne $newHash) {
        throw "Backup/stage verification failed: $relative"
    }
    $records += [pscustomobject]@{ relative = $relative; destination = $destination; backup = $backup; stage = $stage; old_hash = $oldHash; new_hash = $newHash }
}
$replaced = [Collections.Generic.List[object]]::new()
try {
    if (Get-Process PEAK -ErrorAction SilentlyContinue) { throw 'PEAK started during staging; installation cancelled.' }
    foreach ($record in $records) {
        # Replace the directory entry instead of modifying a Gale cache hardlink.
        $temporary = $record.destination + '.installing-' + [Guid]::NewGuid().ToString('N')
        Copy-Item -LiteralPath $record.stage -Destination $temporary
        [IO.File]::Move($temporary, $record.destination, $true)
        $replaced.Add($record)
        if ((Get-FileHash -LiteralPath $record.destination).Hash -ne $record.new_hash) { throw "Installed hash mismatch: $($record.relative)" }
    }
    foreach ($path in $protected.Keys) {
        if ((Get-FileHash -LiteralPath $path).Hash -ne $protected[$path]) { throw "Save/config changed during installation: $path" }
    }
    if ([Reflection.AssemblyName]::GetAssemblyName($oldDll).Version.ToString() -ne "$version.0") { throw 'Installed DLL version mismatch.' }
    $result = [ordered]@{
        installed_at_utc = [DateTime]::UtcNow.ToString('o'); version = "$version.0"; previous_version = $oldVersion
        dll_sha256 = (Get-FileHash -LiteralPath $oldDll).Hash; profile_root = $ProfileRoot; backup_root = $backupRoot
        all_six_files_verified = $true; protected_save_config_count = $protected.Count; saves_and_config_unchanged = $true
        restart_required = $true; runtime_hot_reload_performed = $false; published = $false; files = $records
    }
    $receipt = Join-Path $installRoot 'installed.json'
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receipt -Encoding utf8
    [pscustomobject]$result | Select-Object version, previous_version, dll_sha256, all_six_files_verified, protected_save_config_count, saves_and_config_unchanged, published | ConvertTo-Json
    Write-Output "Receipt: $receipt"
}
catch {
    $failure = $_
    $rollbackFailures = [Collections.Generic.List[string]]::new()
    foreach ($record in $replaced) {
        try {
            $temporary = $record.destination + '.rollback-' + [Guid]::NewGuid().ToString('N')
            Copy-Item -LiteralPath $record.backup -Destination $temporary
            [IO.File]::Move($temporary, $record.destination, $true)
            if ((Get-FileHash -LiteralPath $record.destination).Hash -ne $record.old_hash) { throw 'Hash mismatch.' }
        }
        catch { $rollbackFailures.Add("$($record.relative): $_") }
    }
    if ($rollbackFailures.Count -gt 0) { throw "Installation failed: $failure; rollback errors: $($rollbackFailures -join '; '). Backups: $backupRoot" }
    throw $failure
}
