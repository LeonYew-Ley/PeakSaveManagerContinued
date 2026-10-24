#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string]$GameManagedPath,
    [Parameter(Mandatory = $true)][string]$BepInExCorePath,
    [string]$PluginPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$PluginPath) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/PeakSaveManager/PeakSaveManager.csproj') -Raw
    $assemblyName = @($project.Project.PropertyGroup.AssemblyName | Where-Object { $_ }) | Select-Object -First 1
    if (!$assemblyName) { $assemblyName = 'PeakSaveManager' }
    $PluginPath = Join-Path $projectRoot "build/$assemblyName.dll"
}
Add-Type -Path (Join-Path $BepInExCorePath 'Mono.Cecil.dll')
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($GameManagedPath)
$resolver.AddSearchDirectory($BepInExCorePath)
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$plugin = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $PluginPath).Path, $parameters)
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameManagedPath 'Assembly-CSharp.dll'), $parameters)
try {
    $references = @($plugin.MainModule.GetMemberReferences() | Where-Object { $_.DeclaringType.Scope.Name -eq 'Assembly-CSharp' })
    foreach ($reference in $references) {
        if (!$reference.Resolve()) { throw "Missing game member: $reference" }
    }
    $types = @($plugin.MainModule.GetTypeReferences() | Where-Object { $_.Scope.Name -eq 'Assembly-CSharp' })
    foreach ($reference in $types) {
        if (!$reference.Resolve()) { throw "Missing game type: $reference" }
    }
    $uses = $game.MainModule.GetType('Item').Methods | Where-Object Name -eq 'CanUsePrimary'
    $usesCalls = @($uses.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.FullName -match 'GetData<OptionableIntItemData>' })
    if ($usesCalls.Count -ne 1) { throw 'The game ItemUses contract changed; review before release.' }
    $ammo = $game.MainModule.GetType('RopeShooter').Methods | Where-Object Name -eq 'get_Ammo'
    if (!($ammo.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.FullName -match 'GetData<IntItemData>' })) {
        throw 'The game rope-shooter ammo contract changed; review before release.'
    }
    $pluginType = $plugin.MainModule.GetType('PEAKSaveManager.SaveManagerPlugin')
    # These private native members are used only to clean up our own failed warp.
    # String-based reflection does not appear in the normal member-reference scan.
    $character = $game.MainModule.GetType('Character')
    foreach ($contract in @(
        @{ Name = '_warpRoutine'; Type = 'UnityEngine.Coroutine' }
    )) {
        $field = @($character.Fields | Where-Object Name -eq $contract.Name)
        if ($field.Count -ne 1 -or $field[0].FieldType.FullName -ne $contract.Type) {
            throw "Player warp cleanup contract changed: $($contract.Name)"
        }
    }
    $statusProperty = @($character.Properties | Where-Object { $_.Name -eq 'statusesLocked' -and $_.PropertyType.FullName -eq 'System.Boolean' -and $_.SetMethod })
    $legacyLock = @($character.Fields | Where-Object { $_.Name -eq '_warpStatusLock' -and $_.FieldType.FullName -eq 'System.Boolean' })
    if ($statusProperty.Count -ne 1 -and $legacyLock.Count -ne 1) {
        throw 'Player warp status-lock contract changed.'
    }
    $mapService = $game.MainModule.GetType('MapBundleService')
    if (!($mapService.Methods | Where-Object { $_.Name -eq 'LoadBundlesNearestIndex' -and ($_.Parameters.ParameterType.FullName -join ',') -eq 'System.Int32,System.Boolean' })) {
        throw 'Saved map bundle loading contract changed.'
    }
    $mapData = $mapService.NestedTypes | Where-Object Name -eq 'MapBundleData'
    if (!($mapData.Properties | Where-Object { $_.Name -eq 'Loading' -and $_.PropertyType.FullName -eq 'System.Boolean' -and $_.GetMethod })) {
        throw 'Saved map bundle loading status changed.'
    }
    $warping = @($character.Properties | Where-Object Name -eq 'warping')
    if ($warping.Count -ne 1 -or !$warping[0].SetMethod -or $warping[0].PropertyType.FullName -ne 'System.Boolean') {
        throw 'Player warp cleanup setter changed.'
    }
    if (!($character.Methods | Where-Object { $_.Name -eq 'RecalculateLookDirections' -and $_.Parameters.Count -eq 0 })) {
        throw 'Player view restoration contract changed.'
    }
    $inventory = $pluginType.Methods | Where-Object Name -eq 'ApplyInventory'
    $instructions = $inventory.Body.Instructions
    $resolvePrefab = @($instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Boolean ItemDatabase::TryGetItem(System.String,Item&)' })
    $clear = @($instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'SaveManagerPlugin::ClearBackpackSlot\(' })
    $set = @($instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Void ItemSlot::SetItem(Item,ItemInstanceData)' })
    $sync = @($instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'InventorySyncData::.ctor' })
    if ($resolvePrefab.Count -ne 1 -or $clear.Count -ne 1 -or $set.Count -ne 1 -or $sync.Count -ne 1 -or
        $resolvePrefab[0].Offset -ge $clear[0].Offset -or $set[0].Offset -ge $sync[0].Offset) {
        throw 'Backpack prefab validation/restore must precede inventory clearing/synchronization.'
    }
    $managedRestores = @($pluginType.Methods | Where-Object { $_.Name -eq 'ApplyItemUsageData' -and $_.Parameters[0].ParameterType.FullName -eq 'ItemInstanceData' })
    if ($managedRestores.Count -ne 2) { throw 'Missing inventory or ground-item restore path.' }
    foreach ($restore in $managedRestores) {
        if (!($restore.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'ItemUsesRestoration::ApplyLegacy' })) {
            throw "Restore path is missing the typed fallback: $restore"
        }
    }
    $manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'manifest.json') -Raw | ConvertFrom-Json
    $attribute = $pluginType.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' }
    if ($attribute.ConstructorArguments[0].Value -ne 'com.lucasandersen.peakallyouneedsavemanager') { throw 'Original GUID/config compatibility changed.' }
    if ($attribute.ConstructorArguments[2].Value -ne $manifest.version_number) { throw 'Plugin and manifest versions differ.' }
    Write-Output "$($references.Count) game member references and $($types.Count) game type references resolved."
    Write-Output 'Backpack call order, both typed restore paths, game usage contracts and plugin identity checks passed. Unity/Photon gameplay was not exercised.'
}
finally {
    $plugin.Dispose()
    $game.Dispose()
    $resolver.Dispose()
}
