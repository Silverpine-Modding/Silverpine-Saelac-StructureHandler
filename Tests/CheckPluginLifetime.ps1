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
    Assert-MethodCalls 'StructureTransfer' 'RemoveGameObjects' 'StructureReleasedPoolSavePatch::MarkReleased'
    Assert-MethodCalls 'StructurePoolClaimPatch' 'Postfix' 'StructureReleasedPoolSavePatch::MarkClaimed'
    Assert-MethodCalls 'StructurePrepareNativeSavePatch' 'Prefix' 'StructureSaveState::PrepareNativeSave'
    Assert-MethodCalls 'StructureSaveState' 'TrackAppearance' 'NativeAppearance::TryApply'
    Assert-MethodCalls 'StructureSaveState' 'PrepareNativeSave' 'StructureSaveState::RestoreAppearances'
    Assert-MethodCalls 'StructureTransfer' 'ApplyLoadedImport' 'DestinationGround::Capture'
    Assert-MethodCalls 'StructureTransfer' 'ApplyLoadedImport' 'DestinationSupportPolicy::GetUnreplacedCells'
    $transfer = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.StructureTransfer'
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
    foreach ($required in @('StructureReleasedPoolSavePatch', 'StructurePoolClaimPatch', 'StructurePrepareNativeSavePatch', 'StructureRestoredGroundPatch')) {
        if ($installed -notcontains $required) { throw "Missing startup patch: $required" }
    }
    'PASS Compiled pool discard/reuse and pre-save appearance migration hooks are wired and installed.'
    $gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $projectRoot '../../Silverpine_Data/Managed/Assembly-CSharp.dll'))
    try {
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
