$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$cecilPath = Join-Path $projectRoot '..\..\BepInEx\core\Mono.Cecil.dll'
$pluginPath = Join-Path $projectRoot 'bin\Release\netstandard2.1\StructureHandler.dll'
Add-Type -LiteralPath (Resolve-Path -LiteralPath $cecilPath).Path
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $pluginPath).Path)
try {
    $plugin = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.Plugin'
    $destroy = $plugin.Methods | Where-Object Name -eq 'OnDestroy'
    $calls = @($destroy.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
    if ($calls | Where-Object { $_ -match 'Shutdown|Dispose|Unregister|Unpatch|remove_' }) {
        throw 'Bootstrap OnDestroy still tears down a process-lifetime hook.'
    }
    $saveState = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.StructureSaveState'
    $initialize = $saveState.Methods | Where-Object Name -eq 'Initialize'
    $saveCalls = @($initialize.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
    if (-not ($saveCalls | Where-Object { $_ -match 'ModSaveData::Register' }) -or
        -not ($saveCalls | Where-Object { $_ -match 'ModSaveData::add_Loaded' })) {
        throw 'Persistent save registration or load notification is missing.'
    }
    'PASS Compiled plugin preserves save registration and scene hooks through bootstrap-host destruction.'
    if ($plugin.Fields.Name -contains 'ProtectedWorldTilesConfig' -or
        $plugin.Fields.Name -contains 'BuildableWorldTilesConfig' -or
        $plugin.Methods.Name -contains 'LoadProtectedWorldTiles' -or
        $plugin.Methods.Name -contains 'LoadBuildableWorldTiles') {
        throw 'Unscoped legacy global tile migration is still available.'
    }
    'PASS Global tile configuration cannot leak into loaded saves.'
    function Assert-MethodCalls([string]$type, [string]$method, [string]$expected) {
        $target = $assembly.MainModule.Types | Where-Object FullName -eq ('StructureHandler.' + $type)
        $body = $target.Methods | Where-Object Name -eq $method
        $references = @($body.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
        if (-not ($references | Where-Object { $_ -match $expected })) { throw "$type.$method does not call $expected" }
    }
    Assert-MethodCalls 'StructureReleasedPoolSavePatch' 'Postfix' 'NativeSavePolicy::RemoveReleased'
    Assert-MethodCalls 'StructureSaveLoadPatch' 'Prefix' 'TerrainRepair::RecordSavePath'
    Assert-MethodCalls 'StructurePrepareNativeSavePatch' 'Postfix' 'TerrainRepair::RecordSavePath'
    Assert-MethodCalls 'StructureSaveState' 'Reset' 'TerrainRepair::Reset'
    Assert-MethodCalls 'StructureSaveState' 'ProcessLoadedTile' 'TerrainRepair::CheckAfterLoad'
    Assert-MethodCalls 'StructurePrepareNativeSavePatch' 'Prefix' 'StructureSaveState::PrepareNativeSave'
    Assert-MethodCalls 'StructureSaveState' 'TrackAppearance' 'NativeAppearance::TryApply'
    Assert-MethodCalls 'StructureSaveState' 'PrepareNativeSave' 'StructureSaveState::RestoreAppearances'
    Assert-MethodCalls 'StructureTransfer' 'ApplyLoadedImport' 'DestinationGround::Capture'
    Assert-MethodCalls 'StructureTransfer' 'ApplyLoadedImport' 'DestinationSupportPolicy::GetUnreplacedCells'
    $transfer = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.StructureTransfer'
    $visualRefresh = $transfer.Methods | Where-Object Name -eq 'RequestTileVisualRefresh'
    $request = @($visualRefresh.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and
        $_.Operand.DeclaringType.FullName -eq 'Silverpine.ModdingTools.ModTileVisuals'
    })
    if ($request.Count -ne 1 -or $request[0].Operand.Name -ne 'RequestRefresh' -or
        $request[0].Operand.Parameters[0].ParameterType.FullName -ne 'System.Collections.Generic.IEnumerable`1<UnityEngine.Vector2Int>' -or
        $request[0].Previous.OpCode.Name -ne 'ldc.i4.1' -or
        $request[0].Previous.Previous.OpCode.Name -ne 'ldarg.0') {
        throw 'Importer must defer one exact-cell batch to ModTileVisuals with neighbors enabled.'
    }
    'PASS Imported turf cells go directly to the deferred framework API with neighbor/corner refresh enabled.'
    $applyImport = $transfer.Methods | Where-Object Name -eq 'ApplyLoadedImport'
    $refreshCall = @($applyImport.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RequestTileVisualRefresh'
    })
    if ($refreshCall.Count -ne 1) { throw 'Import must schedule one visual refresh per transaction.' }
    $refreshFinally = @($applyImport.Body.ExceptionHandlers | Where-Object {
        $_.HandlerType.ToString() -eq 'Finally' -and
        $refreshCall[0].Offset -ge $_.HandlerStart.Offset -and $refreshCall[0].Offset -lt $_.HandlerEnd.Offset
    })
    if ($refreshFinally.Count -ne 1 -or $refreshCall[0].Previous.Operand.Name -ne 'occupied') {
        throw 'Visual refresh must run on the exact occupied cells after commit or rollback.'
    }
    $finallyBody = @($applyImport.Body.Instructions | Where-Object {
        $_.Offset -ge $refreshFinally[0].HandlerStart.Offset -and $_.Offset -lt $refreshCall[0].Offset
    })
    if (-not ($finallyBody | Where-Object { $_.OpCode.Name -eq 'stsfld' -and $_.Operand.Name -eq 'Mutating' }) -or
        -not ($finallyBody | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'removingOriginals' })) {
        throw 'Refresh must clear the mutation guard first and skip untouched imports.'
    }
    Assert-MethodCalls 'StructureTransfer' 'ImportAll' 'StructureTransfer::ApplyLoadedImport'
    Assert-MethodCalls 'StructureSaveState' 'ProcessLoadedTile' 'StructureTransfer::ApplyLoadedImport'
    'PASS Immediate and queued imports refresh after their transaction; rollback is covered without refreshing untouched plans.'
    $refreshCatch = @($visualRefresh.Body.ExceptionHandlers | Where-Object {
        $_.HandlerType.ToString() -eq 'Catch' -and $_.CatchType.FullName -eq 'System.Exception'
    })
    if ($refreshCatch.Count -ne 1 -or ($visualRefresh.Body.Instructions | Where-Object {
        $_.OpCode.Name -in @('throw', 'rethrow', 'stfld', 'stsfld')
    })) { throw 'Visual-service failures must not mutate import state or escape into the queued-import result.' }
    Assert-MethodCalls 'StructureTransfer' 'RequestTileVisualRefresh' 'FrameworkDiagnostics::Report'
    'PASS Visual refresh errors are isolated and reported rather than replaying committed imports or masking rollback errors.'
    $dependency = @($plugin.CustomAttributes | Where-Object {
        $_.AttributeType.FullName -eq 'BepInEx.BepInDependency' -and
        $_.ConstructorArguments[0].Value -eq 'renegadex.silverpine.moddingtools'
    })
    if ($dependency.Count -ne 1 -or $dependency[0].ConstructorArguments[1].Value -ne '1.12.0') {
        throw 'The new tile-refresh API requires an enforced ModdingTools 1.12.0 dependency.'
    }
    $framework = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $projectRoot '../ModdingTools/bin/Release/netstandard2.1/ModdingTools.dll'))
    try {
        $visuals = $framework.MainModule.Types | Where-Object FullName -eq 'Silverpine.ModdingTools.ModTileVisuals'
        $batchApi = @($visuals.Methods | Where-Object {
            $_.IsPublic -and $_.IsStatic -and $_.Name -eq 'RequestRefresh' -and $_.Parameters.Count -eq 2 -and
            $_.Parameters[0].ParameterType.FullName -eq 'System.Collections.Generic.IEnumerable`1<UnityEngine.Vector2Int>' -and
            $_.Parameters[1].ParameterType.FullName -eq 'System.Boolean'
        })
        if ($framework.Name.Version -lt [version]'1.12.0.0' -or $batchApi.Count -ne 1) {
            throw 'Compiled framework does not expose the required tile-refresh batch contract.'
        }
    } finally { $framework.Dispose() }
    'PASS The enforced dependency and compiled framework expose the required ModdingTools 1.12.0 tile-refresh API.'
    $commitCalls = @($transfer.NestedTypes.Methods | Where-Object Name -like '<ApplyLoadedImport>*' |
        ForEach-Object { $_.Body.Instructions } | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } |
        ForEach-Object { $_.Operand.FullName })
    if (-not ($commitCalls | Where-Object { $_ -match 'DestinationGround::Apply' }) -or
        -not ($commitCalls | Where-Object { $_ -match 'ImportFootprint::Record' })) {
        throw 'Import commit does not apply destination ground and record the exact footprint.'
    }
    $commit = @($transfer.NestedTypes.Methods | Where-Object {
        $_.Name -like '<ApplyLoadedImport>*' -and
        @($_.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -match 'DestinationGround::Apply'
        }).Count -gt 0
    })
    if ($commit.Count -ne 1) { throw 'Could not identify the import commit callback.' }
    $orderedCalls = @($commit[0].Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference]
    } | ForEach-Object { $_.Operand.FullName })
    $carried = [Array]::FindIndex($orderedCalls, [Predicate[object]]{ param($call) $call -match 'StructureTransfer::InstantiateObjectRecords' })
    $incomingTerrain = [Array]::FindIndex($orderedCalls, [Predicate[object]]{ param($call) $call -match 'StructureTransfer::InstantiateSupportingTerrain' })
    if ($carried -lt 0 -or $carried -ge $incomingTerrain -or
        -not ($orderedCalls | Where-Object { $_ -match 'System.Linq.Enumerable::Skip' })) {
        throw 'Destination support must be instantiated before imports and excluded from floor history rebasing.'
    }
    'PASS Compiled importer carries destination terrain before imported objects and preserves its floor history.'
    $collect = $transfer.Methods | Where-Object Name -eq 'CollectObjectsAt'
    $collectCalls = @($collect.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference]
    } | ForEach-Object { $_.Operand.FullName })
    if ($collect.Parameters.Count -ne 2 -or
        -not ($collectCalls | Where-Object { $_ -match 'GetComponent<StructureHandler.StructureSupplementalInstance>' }) -or
        -not ($collectCalls | Where-Object { $_ -match 'FindObjectsOfType<StructureHandler.StructureSupplementalInstance>' }) -or
        -not ($collectCalls | Where-Object { $_ -match 'HashSet`1<System.String>::Contains' })) {
        throw 'Import replacement does not recognize supplemental objects and matching nonserialized prefabs.'
    }
    foreach ($excluded in @('NeuralNPC', 'EntityMover', 'IPersistentSerializableMonoBehavior', 'PersistentEntity')) {
        if (-not ($collectCalls | Where-Object { $_ -match ('GetComponent<' + $excluded + '>') })) {
            throw "Import replacement lost the $excluded exclusion."
        }
    }
    'PASS Compiled replacement scan includes nonserialized wells and preserves character/persistent exclusions.'
    Assert-MethodCalls 'StructureSaveState' 'Reset' 'ImportFootprint::Reset'
    Assert-MethodCalls 'StructureSaveState' 'TileCleared' 'ImportFootprint::ClearWorldTile'
    Assert-MethodCalls 'DestinationGround' 'InstantiateRestoredGround' 'StructureTerrainPersistence::RecordRestoredGround'
    $awake = $plugin.Methods | Where-Object Name -eq 'Awake'
    $installed = @($awake.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.TypeReference] } | ForEach-Object { $_.Operand.Name })
    foreach ($required in @('StructureReleasedPoolSavePatch', 'StructurePrepareNativeSavePatch', 'StructureRestoredGroundPatch', 'PlayerBuildableSleepPatch', 'PlayerBuildablePickupPatch')) {
        if ($installed -notcontains $required) { throw "Missing startup patch: $required" }
    }
    'PASS Compiled pool filtering, per-save repair detection, and save tracking hooks are wired and installed.'
    Assert-MethodCalls 'PlayerBuildableSleepPatch' 'GetSleepOwner' 'Plugin::GetWorldTile'
    Assert-MethodCalls 'PlayerBuildableSleepPatch' 'GetSleepOwner' 'Plugin::IsWorldTilePlayerBuildable'
    Assert-MethodCalls 'PlayerBuildableSleepPatch' 'IsSleepingPlot' 'Plugin::GetWorldTile'
    Assert-MethodCalls 'PlayerBuildableSleepPatch' 'IsSleepingPlot' 'Plugin::IsWorldTilePlayerBuildable'
    Assert-MethodCalls 'PlayerBuildableSleepPatch' 'IsSleepingPlot' 'WorldInfoManager::IsOnPlayerOwnedPlot'
    $sleep = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.PlayerBuildableSleepPatch'
    $sleepPermission = $sleep.Methods | Where-Object { $_.Name -in @('GetSleepOwner', 'IsSleepingPlot', 'ResolveSleepOwner') }
    if ($sleepPermission.Body.Instructions | Where-Object { $_.OpCode.Name -in @('stfld', 'stsfld') }) {
        throw 'Sleeping permission must not mutate saved bed ownership or global state.'
    }
    $sleepTarget = $sleep.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' }
    if ($sleepTarget.ConstructorArguments[0].Value.FullName -ne 'Bed' -or
        $sleepTarget.ConstructorArguments[1].Value -ne 'OnInteract') { throw 'Sleep patch must target player bed interaction only.' }
    'PASS Sleep permission uses the bed tile, preserves native plot fallback and never mutates saved owners.'
    Assert-MethodCalls 'PlayerBuildablePickupPatch' 'IsOwnershipBlocked' 'Plugin::GetWorldTile'
    Assert-MethodCalls 'PlayerBuildablePickupPatch' 'IsOwnershipBlocked' 'Plugin::IsWorldTilePlayerBuildable'
    Assert-MethodCalls 'PlayerBuildablePickupPatch' 'IsOwnershipBlocked' 'UnityExtensions::GetVector2IntPosition'
    $pickupPatch = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.PlayerBuildablePickupPatch'
    $pickupPermission = $pickupPatch.Methods | Where-Object { $_.Name -in @('IsOwnershipBlocked', 'ResolveOwnershipBlock') }
    if ($pickupPermission.Body.Instructions | Where-Object { $_.OpCode.Name -in @('stfld', 'stsfld') }) {
        throw 'Pickup permission must not mutate ownership or save/global state.'
    }
    'PASS Compiled pickup permission uses furniture coordinates and does not clear saved ownership.'
    $policy = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.NativeSavePolicy'
    if (($policy.Methods | Where-Object Name -eq 'RemoveReleased').Parameters.Count -ne 3) {
        throw 'Pool saving still depends on coordinates or historical import markers.'
    }
    $repair = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.TerrainRepair'
    $applyCalls = @(($repair.Methods | Where-Object Name -eq 'Apply').Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference]
    } | ForEach-Object { $_.Operand.FullName })
    $backupIndex = [Array]::FindIndex($applyCalls, [Predicate[object]] { param($call) $call -match 'TerrainRepairBackup::Create' })
    $mutationIndex = [Array]::FindIndex($applyCalls, [Predicate[object]] { param($call) $call -match 'ImportTransaction::Execute' })
    if ($backupIndex -lt 0 -or $mutationIndex -le $backupIndex) { throw 'Repair can mutate the world before a complete backup.' }
    $checkCalls = @(($repair.Methods | Where-Object Name -eq 'CheckAfterLoad').Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference]
    } | ForEach-Object { $_.Operand.FullName })
    if ($checkCalls | Where-Object { $_ -match '::Apply\(|RemoveGameObjects|::Release\(|::Save\(' }) {
        throw 'Load-time repair detection must be read-only.'
    }
    'PASS Repair detection is read-only and confirmed repair creates its checkpoint before mutation.'
    Assert-MethodCalls 'TerrainRepair' 'Inspect' 'TerrainRepairDecorations::IsNativeOvergrowth'
    Assert-MethodCalls 'TerrainRepair' 'GrassSafetyReason' 'TerrainRepairDecorations::IsNativeOvergrowth'
    Assert-MethodCalls 'TerrainRepairDecorations' 'IsNativeOvergrowth' 'List`1<UnityEngine.GameObject>::Contains'
    Assert-MethodCalls 'TerrainRepairDecorations' 'IsNativeOvergrowth' 'HashSet`1<System.Type>::SetEquals'
    Assert-MethodCalls 'TerrainRepairDecorations' 'IsNativeOvergrowth' 'Transform::get_parent'
    Assert-MethodCalls 'TerrainRepairDecorations' 'IsNativeOvergrowth' 'Transform::get_childCount'
    Assert-MethodCalls 'TerrainRepairDecorations' 'IsNativeOvergrowth' 'GameObject::GetComponent<OvergrowthTile>'
    'PASS Native grass edging requires ownership, native components, and the correct child hierarchy in both repair scans.'
    $gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $projectRoot '../../Silverpine_Data/Managed/Assembly-CSharp.dll'))
    try {
        $bed = $gameAssembly.MainModule.Types | Where-Object Name -eq 'Bed'
        $interact = $bed.Methods | Where-Object Name -eq 'OnInteract'
        $ownerReads = @($interact.Body.Instructions | Where-Object {
            $_.OpCode.Name -eq 'ldfld' -and $_.Operand -is [Mono.Cecil.FieldReference] -and
            $_.Operand.DeclaringType.Name -eq 'Bed' -and $_.Operand.Name -eq 'owner'
        })
        $bedCalls = @($interact.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference]
        } | ForEach-Object { $_.Operand.FullName })
        if ($ownerReads.Count -ne 3 -or @($bedCalls | Where-Object { $_ -match 'WorldInfoManager::IsOnPlayerOwnedPlot' }).Count -ne 1 -or
            -not ($bedCalls | Where-Object { $_ -match 'TileTags::HasTileTag' }) -or
            -not ($bedCalls | Where-Object { $_ -match 'HasStatusEffectOfType<StatusEffect_Cold>' })) {
            throw 'Native bed permission, roof, or cold checks changed; sleep patch requires review.'
        }
        'PASS Actual native bed IL matches the scoped ownership patch and retains independent roof/cold checks.'
        $pickup = $gameAssembly.MainModule.Types | Where-Object Name -eq 'Pickupable'
        foreach ($methodName in @('SetEnabled', 'OnMouseUpAsButton')) {
            $method = $pickup.Methods | Where-Object Name -eq $methodName
            $ownedReads = @($method.Body.Instructions | Where-Object {
                $_.OpCode.Name -eq 'ldfld' -and $_.Operand -is [Mono.Cecil.FieldReference] -and
                $_.Operand.DeclaringType.Name -eq 'Pickupable' -and $_.Operand.Name -eq 'owned'
            })
            if ($ownedReads.Count -ne 1) { throw "Native $methodName pickup ownership check changed." }
        }
        $highlight = $pickup.Methods | Where-Object Name -eq 'SetEnabled'
        $pickupClick = $pickup.Methods | Where-Object Name -eq 'OnMouseUpAsButton'
        $pickupCalls = @($pickupClick.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
        if (-not ($highlight.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldc.r4' -and [Math]::Abs([double]$_.Operand - 1.55) -lt 0.001 }) -or
            -not ($pickupClick.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'active' }) -or
            -not ($pickupCalls | Where-Object { $_ -match 'Inventory::TryAddItem' }) -or
            -not ($pickupCalls | Where-Object { $_ -match 'Pickupable::GenerateItem' })) {
            throw 'Native pickup distance, mode, inventory or item-generation behavior changed.'
        }
        'PASS Actual native pickup IL matches both permission hooks and retains distance, active-mode and inventory checks.'
        $overgrowth = $gameAssembly.MainModule.Types | Where-Object FullName -eq 'OvergrowthTile'
        $owned = $overgrowth.Fields | Where-Object Name -eq 'instantiatedPrefabs'
        $sourcePrefab = $overgrowth.Fields | Where-Object Name -eq 'overgrowthPrefab'
        if ($owned.FieldType.FullName -ne 'System.Collections.Generic.List`1<UnityEngine.GameObject>' -or
            $sourcePrefab.FieldType.FullName -ne 'UnityEngine.GameObject') {
            throw 'The native overgrowth ownership fields changed; repair decoration matching requires review.'
        }
        $cleanupCalls = @(($overgrowth.Methods | Where-Object Name -eq 'OnObjectPoolDestroy').Body.Instructions |
            Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
        if (-not ($cleanupCalls | Where-Object { $_ -match 'OvergrowthTile::DestroyOvergrowth' })) {
            throw 'Releasing native grass no longer removes its owned edging.'
        }
        'PASS Actual game exposes the expected overgrowth ownership and pool-release cleanup.'
        $handler = $gameAssembly.MainModule.Types | Where-Object FullName -eq 'GrassTileHandler'
        $deconstruct = $handler.Methods | Where-Object Name -eq 'OnDeconstructed'
        $instantiate = @($deconstruct.Body.Instructions | Where-Object {
            $_.OpCode.Name -eq 'call' -and $_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and
            $_.Operand.Name -eq 'Instantiate' -and $_.Operand.DeclaringType.FullName -eq 'UnityEngine.Object' -and
            $_.Operand.GenericArguments.Count -eq 1 -and $_.Operand.GenericArguments[0].FullName -eq 'UnityEngine.GameObject' -and
            $_.Operand.Parameters.Count -eq 3 -and $_.Operand.Parameters[1].ParameterType.FullName -eq 'UnityEngine.Vector3' -and
            $_.Operand.Parameters[2].ParameterType.FullName -eq 'UnityEngine.Quaternion'
        })
        if ($instantiate.Count -ne 1) { throw 'Native floor restoration no longer matches the scoped ground patch.' }
        'PASS Actual game floor-deconstruction IL has exactly one compatible terrain instantiation.'
    } finally { $gameAssembly.Dispose() }
} finally { $assembly.Dispose() }
