#Requires -Version 7.0
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../dist'))
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'manifest.json') -Raw | ConvertFrom-Json
$name = $manifest.name
$version = $manifest.version_number
if ($name -ne 'The_All_You_Need_Save_Manager_Continued' -or $version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package identity/version.' }
if ($manifest.description.Length -gt 250) { throw 'Description exceeds Thunderstore limit.' }
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/PeakSaveManager/PeakSaveManager.csproj') -Raw
$projectVersion = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($projectVersion -ne $version) { throw 'Project and manifest versions differ.' }
$config = Get-Content -LiteralPath (Join-Path $projectRoot 'thunderstore.toml') -Raw
foreach ($line in @('namespace = "LeonYew"', "name = `"$name`"", "versionNumber = `"$version`"")) {
    if (!$config.Contains($line)) { throw "Thunderstore config mismatch: $line" }
}
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Join-Path $projectRoot 'icon.png'))
try { if ($icon.Width -ne 256 -or $icon.Height -ne 256) { throw 'Icon must be 256x256.' } } finally { $icon.Dispose() }
$dll = Join-Path $projectRoot 'build/PeakSaveManagerContinued.dll'
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion -ne "$version.0" -or
    [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString() -ne "$version.0") { throw 'DLL and manifest versions differ.' }
$packageFiles = [ordered]@{}
foreach ($file in @('manifest.json', 'README.md', 'CHANGELOG.md', 'NOTICE.md', 'icon.png')) { $packageFiles[$file] = Join-Path $projectRoot $file }
$dllEntry = 'BepInEx/plugins/PeakSaveManagerContinued/PeakSaveManagerContinued.dll'
$packageFiles[$dllEntry] = $dll
$sourceFiles = [ordered]@{}
foreach ($file in @(
    'README.md', 'CHANGELOG.md', 'NOTICE.md', 'RELEASE_NOTES.md', 'CONTRIBUTING.md', 'manifest.json',
    '.gitignore', '.gitattributes', 'thunderstore.toml', 'icon.png', 'assets/icon-original.png', 'Build.ps1',
    'src/PeakSaveManager/PeakSaveManager.csproj', 'src/PeakSaveManager/SaveManagerPlugin.cs',
    'src/PeakSaveManager/SaveModels.cs', 'src/PeakSaveManager/ItemUsesRestoration.cs',
    'src/PeakSaveManager/PlayerLoadRestoration.cs', 'src/PeakSaveManager/PlayerLoadOwner.cs',
    'src/PeakSaveManager/PlayerRestoration.cs',
    'src/PeakSaveManager/SavedMapRestoration.cs', 'src/PeakSaveManager/SavedMapLoader.cs',
    'src/PeakSaveManager/WarpStatusLock.cs',
    'tools/Verify-ItemUsageData.ps1', 'tools/Verify-Compatibility.ps1', 'tools/Verify-PlayerRestoration.ps1',
    'tools/SaveVerificationTests/SaveVerificationTests.csproj', 'tools/SaveVerificationTests/Program.cs',
    'tools/SaveVerificationTests/PlayerLoadRestorationTests.cs', 'tools/SaveVerificationTests/PlayerRestorationTests.cs',
    'tools/SaveVerificationTests/MergedPlayerLoadTests.cs',
    'tools/SaveVerificationTests/SavedMapRestorationTests.cs', 'tools/SaveVerificationTests/WarpStatusLockTests.cs',
    'scripts/create-icon.py', 'scripts/package.ps1', 'scripts/publish.ps1',
    '.github/pull_request_template.md', '.github/ISSUE_TEMPLATE/bug_report.yml',
    '.github/ISSUE_TEMPLATE/config.yml', '.github/ISSUE_TEMPLATE/feature_request.yml', '.github/ISSUE_TEMPLATE/help_question.yml'
)) { $sourceFiles[$file] = Join-Path $projectRoot $file }
foreach ($path in @($packageFiles.Values) + @($sourceFiles.Values)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing release file: $path" }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-Zip([string]$Path, [System.Collections.IDictionary]$Files) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in $Files.GetEnumerator()) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.Value, $file.Key) | Out-Null
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}
$packagePath = Join-Path $OutputDirectory "$name-$version.zip"
$sourcePath = Join-Path $OutputDirectory "$name-$version-source.zip"
Write-Zip $packagePath $packageFiles
Write-Zip $sourcePath $sourceFiles
foreach ($pair in @(@{ Path = $packagePath; Files = $packageFiles }, @{ Path = $sourcePath; Files = $sourceFiles })) {
    $archive = [IO.Compression.ZipFile]::OpenRead($pair.Path)
    try {
        $actual = @($archive.Entries.FullName | Sort-Object)
        $expected = @($pair.Files.Keys | Sort-Object)
        if (($actual -join '|') -cne ($expected -join '|')) { throw 'Release archive allowlist mismatch.' }
        foreach ($entry in $archive.Entries) {
            $entryStream = $entry.Open()
            try { $entryHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream)) } finally { $entryStream.Dispose() }
            if ($entryHash -ne (Get-FileHash -LiteralPath $pair.Files[$entry.FullName]).Hash) { throw "Archive content mismatch: $($entry.FullName)" }
        }
    } finally { $archive.Dispose() }
}
[ordered]@{
    Name = $name; Version = $version; Package = $packagePath; Source = $sourcePath
    PackageSHA256 = (Get-FileHash -LiteralPath $packagePath).Hash
    SourceSHA256 = (Get-FileHash -LiteralPath $sourcePath).Hash
    PackageEntries = $packageFiles.Count; SourceEntries = $sourceFiles.Count
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'package-info.json') -Encoding utf8
Write-Output "Verified $($packageFiles.Count)-file mod archive and $($sourceFiles.Count)-file source archive."
Write-Output $packagePath
Write-Output $sourcePath
