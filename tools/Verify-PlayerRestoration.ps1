#Requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string]$GameManagedPath,
    [Parameter(Mandatory = $true)][string]$BepInExCorePath
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Add-Type -Path (Join-Path $BepInExCorePath 'Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameManagedPath 'Assembly-CSharp.dll'))
$plugin = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $projectRoot 'build/PeakSaveManagerContinued.dll'))

function Require-Rpc([string]$TypeName, [string]$MethodName, [string[]]$ParameterTypes) {
    $matches = @($game.MainModule.GetType($TypeName).Methods | Where-Object {
        $_.Name -eq $MethodName -and
        ($_.Parameters.ParameterType.FullName -join ',') -eq ($ParameterTypes -join ',')
    })
    if ($matches.Count -ne 1 -or !($matches[0].CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'Photon.Pun.PunRPC' })) {
        throw "Native RPC contract changed: $TypeName.$MethodName($($ParameterTypes -join ', '))."
    }
    return $matches[0]
}

function Get-Methods($Types) {
    foreach ($type in $Types) {
        $type.Methods
        Get-Methods $type.NestedTypes
    }
}

try {
    $revive = Require-Rpc 'Character' 'ReviveCharacter' @('System.Boolean')
    $dead = Require-Rpc 'Character' 'RPCA_SetDead' @()
    $null = Require-Rpc 'CharacterData' 'RPC_SyncOnJoin' @(
        'System.Boolean', 'System.Boolean', 'System.Boolean', 'System.Boolean', 'Photon.Pun.PhotonView',
        'System.Boolean', 'System.Boolean', 'System.Boolean', 'System.Boolean', 'System.Single', 'System.Single',
        'System.Boolean', 'System.Boolean', 'Photon.Pun.PhotonView', 'Photon.Pun.PhotonView',
        'System.Boolean[]', 'System.Boolean', 'System.Boolean', 'System.Int32')
    $null = Require-Rpc 'CharacterAfflictions' 'RPC_ApplyStatusesFromFloatArray' @('System.Single[]', 'Photon.Pun.PhotonMessageInfo')

    $reviveCalls = @($revive.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] })
    if ($reviveCalls | Where-Object { $_.Operand.Name -in @('DropAllItems', 'WarpPlayer', 'Record', 'UpdateFromRevive') }) {
        throw 'ReviveCharacter acquired inventory, warp or revival-record side effects; review the restore sequence.'
    }
    $collision = @($reviveCalls | Where-Object { $_.Operand.Name -eq 'ToggleCollision' })
    if ($collision.Count -ne 1 -or $collision[0].Previous.OpCode.Name -ne 'ldc.i4.1') {
        throw 'ReviveCharacter no longer enables ragdoll collision.'
    }
    $requiredResets = @('deathTimer', 'passedOut', 'fullyPassedOut', 'fallSeconds')
    foreach ($fieldName in $requiredResets) {
        $writes = @($revive.Body.Instructions | Where-Object {
            $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq $fieldName
        })
        if ($writes.Count -ne 1 -or $writes[0].Previous.OpCode.Name -notin @('ldc.r4', 'ldc.i4.0') -or
            ($writes[0].Previous.Operand -and $writes[0].Previous.Operand -ne 0)) {
            throw "ReviveCharacter no longer resets $fieldName to zero."
        }
    }
    $postStatus = @($reviveCalls | Where-Object { $_.Operand.Name -eq 'ApplyPostReviveStatus' })
    $conditional = @($revive.Body.Instructions | Where-Object {
        $_.Previous -and $_.Previous.OpCode.Name -eq 'ldarg.1' -and $_.OpCode.Name -like 'brfalse*'
    })
    if ($postStatus.Count -ne 1 -or $conditional.Count -ne 1 -or $conditional[0].Operand.Offset -le $postStatus[0].Offset) {
        throw 'The applyStatus=false contract no longer skips post-revive penalties.'
    }
    if (!($dead.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ToggleCollision'
    })) { throw 'RPCA_SetDead no longer handles corpse collision.' }

    $methods = @(Get-Methods $plugin.MainModule.Types | Where-Object HasBody)
    $coordinatorCalls = @($methods | ForEach-Object { $_.Body.Instructions } | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -like '*PlayerRestoration::ApplyBeforeWarp(*'
    })
    if ($coordinatorCalls.Count -ne 1) { throw 'The live player-load coroutine must use the behavior-tested restore coordinator exactly once.' }
    $lifeFlagRestore = $plugin.MainModule.GetType('PEAKSaveManager.SaveManagerPlugin').Methods | Where-Object Name -eq 'RestoreSavedLifeFlags'
    if (!($lifeFlagRestore.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq 'RPC_SyncOnJoin' })) {
        throw 'Saved unconscious/dead flags are no longer synchronized through the native RPC.'
    }
    $ownerType = $plugin.MainModule.GetType('PEAKSaveManager.SaveManagerPlugin/UnityPlayerLoadTarget')
    $ownerLifeRestore = $ownerType.Methods | Where-Object Name -eq 'RestoreLifeState'
    foreach ($required in @('ToggleCollision', 'ApplyCharacterStatuses', 'SetExtraStamina', 'PushAll')) {
        if (!($ownerLifeRestore.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $required })) {
            throw "The owner must finalize saved life state after warp: missing $required."
        }
    }
    Write-Output 'Native life/state/status RPC signatures, collision/timer resets, penalty bypass and compiled restore-coordinator integration passed.'
    Write-Output 'Only managed/IL contracts were checked. Two-player Unity/Photon acceptance is still required.'
}
finally {
    $plugin.Dispose()
    $game.Dispose()
}
