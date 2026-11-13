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
    [xml]$projectXml = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'StructureHandler.csproj')
    $projectVersion = [string]$projectXml.Project.PropertyGroup.Version
    $pluginVersion = ($plugin.Fields | Where-Object Name -eq 'PluginVersion').Constant
    $pluginAttribute = $plugin.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInPlugin' }
    if ($pluginVersion -ne $projectVersion -or $pluginAttribute.ConstructorArguments[2].Value -ne $projectVersion -or
        $assembly.Name.Version.ToString(3) -ne $projectVersion) { throw 'Plugin, assembly and project versions disagree.' }
    'PASS BepInEx startup identity, compiled assembly and project version agree.'
    Assert-MethodCalls 'Plugin' 'Awake' 'MarketCrates::Install'
    foreach ($method in @('Reset','Capture','Restore')) {
        Assert-MethodCalls 'StructureSaveState' $method ('MarketCrates::' + $method)
    }
    Assert-MethodCalls 'StructureSaveState' 'OnLoaded' 'MarketCrates::Reconcile'
    $markets = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.MarketCrates'
    $marketInstall = $markets.Methods | Where-Object Name -eq 'Install'
    $marketPatches = @($marketInstall.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.TypeReference] } | ForEach-Object { $_.Operand.Name })
    foreach ($required in @('MarketCrateIdentityStartPatch','MarketCrateIdentityDestroyPatch','MarketCrateStartPatch',
        'MarketCrateDeserializePatch','MarketCrateDestroyPatch','MarketCrateHourPatch','MarketCrateBoardPatch')) {
        if ($marketPatches -notcontains $required) { throw "Missing shared market patch: $required" }
    }
    foreach ($methodName in @('RemoveIdentity','Unregister')) {
        $body = $markets.Methods | Where-Object Name -eq $methodName
        # Release compilation folds ReferenceEquals into a reference branch.
        $ownerGuard = @($body.Body.Instructions | Where-Object {
            $_.OpCode.Name -like 'bne.un*' -and $_.Previous.Operand -is [Mono.Cecil.MethodReference] -and
            $_.Previous.Operand.Name -eq 'get_gameObject'
        })
        $removeKey = @($body.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Remove' -and
            $_.Operand.DeclaringType.Name -eq 'Dictionary`2'
        })
        if ($ownerGuard.Count -ne 1 -or $removeKey.Count -ne 1 -or $ownerGuard[0].Offset -ge $removeKey[0].Offset -or
            $ownerGuard[0].Operand.Offset -le $removeKey[0].Offset) { throw "$methodName lost its identity-owner guard." }
    }
    Assert-MethodCalls 'MarketCrates' 'RemoveIdentity' 'MarketCrates::PromoteIdentity'
    Assert-MethodCalls 'MarketCrates' 'SellAllStock' 'SharedMarketBook::TryBegin'
    Assert-MethodCalls 'MarketCrates' 'SellAllStock' 'SharedMarketBook::Bind'
    'PASS Shared markets install all lifecycle hooks, owner-checked identity removal, one-cycle guard and per-save board reset/capture/restore.'
    foreach ($methodName in @('InstantiateSupportingTerrain','InstantiateObjectRecords')) {
        $type = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.StructureTransfer'
        $body = $type.Methods | Where-Object Name -eq $methodName
        $ordered = @($body.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
        $prepareIndex = [Array]::FindIndex($ordered, [Predicate[object]]{ param($call) $call -match 'ImportedPrefabLifecycle::Prepare' })
        $deserializeIndex = [Array]::FindIndex($ordered, [Predicate[object]]{ param($call) $call -match 'StructureTransfer::ApplyComponentRecords' })
        $trackedIndex = [Array]::FindIndex($ordered, [Predicate[object]]{ param($call) $call -match 'ICollection`1<UnityEngine.GameObject>::Add' })
        if ($trackedIndex -lt 0 -or $prepareIndex -le $trackedIndex -or $deserializeIndex -le $prepareIndex) {
            throw "$methodName must track rollback first, then prepare the mod prefab before deserializing components."
        }
    }
    Assert-MethodCalls 'ImportedPrefabLifecycle' 'Prepare' 'SerializablePrefabs::TryGetRegistration'
    Assert-MethodCalls 'ImportedPrefabLifecycle' 'Prepare' 'SerializablePrefabOptions::get_ActivateLoadedInstances'
    Assert-MethodCalls 'ImportedPrefabLifecycle' 'Prepare' 'SerializablePrefabOptions::get_OnInstanceRestored'
    'PASS Terrain/object imports and rollback activate registered mod clones before Deserialize, after recording rollback ownership.'
    Assert-MethodCalls 'Plugin' 'Awake' 'WaterExportSettings::Bind'
    Assert-MethodCalls 'StructureTransfer' 'AddTerrainRecord' 'WaterExportPolicy::ShouldIncludeTerrain'
    Assert-MethodCalls 'StructureTransfer' 'FindSupportingTerrain' 'StructureTransfer::AddTerrainRecord'
    $transferType = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.StructureTransfer'
    if ($transferType.Methods.Name -contains 'ShouldImportTerrain' -or $plugin.Fields.Name -contains 'ImportOtherWaterTiles') {
        throw 'Legacy runtime water import filtering is still present.'
    }
    function Get-WaterCheckTypes($types) {
        foreach ($type in $types) { $type; Get-WaterCheckTypes $type.NestedTypes }
    }
    $allWaterCheckMethods = @(Get-WaterCheckTypes $assembly.MainModule.Types | ForEach-Object { $_.Methods } | Where-Object HasBody)
    foreach ($method in $allWaterCheckMethods) {
        foreach ($instruction in $method.Body.Instructions) {
            if ($instruction.Operand -is [Mono.Cecil.MethodReference] -and
                $instruction.Operand.DeclaringType.Name -eq 'WaterExportPolicy' -and
                $method.Name -ne 'AddTerrainRecord') {
                throw "Export filtering escaped into $($method.FullName)."
            }
            if ($instruction.OpCode.Name -eq 'ldsfld' -and $instruction.Operand.Name -eq 'ExportOtherWaterTiles' -and
                $method.Name -ne 'AddTerrainRecord' -and $method.DeclaringType.FullName -notlike 'StructureHandler.StructureControlsUI*') {
                throw "Export setting is read outside export/menu code: $($method.FullName)."
            }
        }
    }
    'PASS Water preference is wired only to world export collection/menu, never import, queue, editor save or persistence paths.'
    Assert-MethodCalls 'StructureEditorUI' 'DrawRecords' 'StructureEditorUI::DrawZoneColors'
    Assert-MethodCalls 'StructureEditorUI' 'DrawZoneColors' 'ZonePreview::Build'
    Assert-MethodCalls 'StructureEditorUI' 'DrawZoneBrushControls' 'GUILayout::Toggle'
    Assert-MethodCalls 'StructureEditorUI' 'Close' 'StructureEditorUI::ClearZoneColors'
    Assert-MethodCalls 'StructureEditorUI' 'OnFrameworkSessionClosed' 'StructureEditorUI::ClearZoneColors'
    $zonePreviewType = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.StructureEditorUI'
    $drawZone = $zonePreviewType.Methods | Where-Object Name -eq 'DrawZoneColors'
    if (-not ($drawZone.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'showZoneColors' }) -or
        ($drawZone.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'MarkStructureDirty' })) {
        throw 'Zone colors must be guarded by their own display-only toggle.'
    }
    'PASS Named-zone colors have an independent display toggle, revision-cached rendering and cache cleanup on close.'
    Assert-MethodCalls 'StructureEditorUI' 'IsPreviewLightingEffect' 'PreviewLightingPolicy::IsLightingEffect'
    Assert-MethodCalls 'StructureEditorUI' 'IsPreviewLightingEffect' 'PreviewLightingPolicy::IsFakeAO'
    Assert-MethodCalls 'StructureEditorUI' 'IsPreviewLightingEffect' 'Transform::get_parent'
    Assert-MethodCalls 'StructureEditorUI' 'AddPreviewCommands' 'PreviewLightingPolicy::IsFakeAO'
    $editor = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.StructureEditorUI'
    $editorMethods = @($editor.Methods) + @($editor.NestedTypes | ForEach-Object { $_.Methods })
    $filterCalls = @($editorMethods | Where-Object HasBody | ForEach-Object { $_.Body.Instructions } | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'IsPreviewLightingEffect'
    })
    if ($filterCalls.Count -eq 0) { throw 'Preview renderer collection no longer calls its lighting filter.' }
    'PASS Preview collection filters FakeAO renderer/sprite identities and parent containers; variant drawing also rejects FakeAO.'
    $filter = $editor.Methods | Where-Object Name -eq 'IsPreviewLightingEffect'
    $mutations = @($filter.Body.Instructions | Where-Object {
        $_.OpCode.Name -in @('stfld', 'stsfld') -or
        ($_.Operand -is [Mono.Cecil.MethodReference] -and
            ($_.Operand.Name -like 'set_*' -or $_.Operand.Name -in @('Destroy', 'SetActive', 'SetPropertyBlock')))
    })
    if ($mutations.Count -gt 0) { throw 'The preview filter must not modify native prefab/rendering state.' }
    'PASS Preview lighting filter only reads prefab metadata; native renderers and materials are not modified.'
    Assert-MethodCalls 'StructureEditorUI' 'DrawSelectedObject' 'StructureEditorUI::DrawFurnitureCollision'
    Assert-MethodCalls 'StructureEditorUI' 'DrawFurnitureCollision' 'FurnitureCollision::TryReadEnabled'
    Assert-MethodCalls 'StructureEditorUI' 'DrawFurnitureCollision' 'FurnitureCollision::SetEnabled'
    Assert-MethodCalls 'StructureEditorUI' 'DrawFurnitureCollision' 'StructureEditorUI::MarkStructureDirty'
    Assert-MethodCalls 'StructureTransfer' 'InstantiateObjectRecords' 'StructureTransfer::ApplyComponentRecords'
    Assert-MethodCalls 'StructureTransfer' 'ApplyComponentRecords' 'ISerializableMonoBehavior::Deserialize'
    Assert-MethodCalls 'StructureTransfer' 'CreateComponentRecords' 'ISerializableMonoBehavior::Serialize'
    'PASS Furniture toggle is wired to undoable component edits and the existing native import/export serializer path.'
    $collisionUI = $editor.Methods | Where-Object Name -eq 'DrawFurnitureCollision'
    $collisionCalls = @($collisionUI.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
    if ($collisionUI.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.DeclaringType.Name -eq 'TurfCollider' }) {
        throw 'Editor collision toggle must not edit native prefab flags.'
    }
    if ($collisionCalls | Where-Object { $_ -match 'AddComponent|Destroy|SetActive|StructureSaveState|ModSaveData' }) {
        throw 'Editor collision toggle must not mutate prefab components or create companion overrides.'
    }
    'PASS Furniture collision editor neither mutates prefab components nor adds companion save overrides.'
    Assert-MethodCalls 'StructureEditorUI' 'GetSpritePreviews' 'SortingLayer::GetLayerValueFromID'
    Assert-MethodCalls 'StructureEditorUI' 'AddPreviewCommands' 'PreviewSortKey::YOrder'
    Assert-MethodCalls 'StructureEditorUI' 'AddPreviewCommands' 'PreviewSortKey::RotationOffset'
    Assert-MethodCalls 'StructureEditorUI' 'EnsureRenderOrder' 'StructureEditorUI::AddPreviewCommands'
    Assert-MethodCalls 'StructureEditorUI' 'DrawRecords' 'StructureEditorUI::EnsureRenderOrder'
    Assert-MethodCalls 'StructureEditorUI' 'DrawPrefabSprite' 'GUIUtility::RotateAroundPivot'
    'PASS Compiled preview uses native layer priorities, global sprite-part ordering, Y offsets and transformed quads.'
    Assert-MethodCalls 'StructureEditorUI' 'DrawPreviewPanel' 'StructureEditorUI::DrawCachedPreview'
    Assert-MethodCalls 'StructureEditorUI' 'DrawCachedPreview' 'StructureEditorUI::ProcessItemPreviewQueue'
    Assert-MethodCalls 'StructureEditorUI' 'DrawCachedPreview' 'StructureEditorUI::RenderPreviewSurface'
    Assert-MethodCalls 'StructureEditorUI' 'DrawCachedPreview' 'StructureEditorUI::DrawPreviewContents'
    Assert-MethodCalls 'StructureEditorUI' 'DrawPrefabSprite' 'StructureEditorUI::DrawPreviewQuad'
    Assert-MethodCalls 'StructureEditorUI' 'ClearRenderCaches' 'StructureEditorUI::ReleasePreviewSurface'
    Assert-MethodCalls 'StructureEditorUI' 'ClearItemEditor' 'EditorWorkQueue`1<StructureHandler.StructureObject>::Clear'
    Assert-MethodCalls 'StructureEditorUI' 'GenerateItemPreview' 'StructureEditorUI::BuildItemVisual'
    Assert-MethodCalls 'StructureEditorUI' 'ProcessItemPreviewQueue' 'Time::get_frameCount'
    Assert-MethodCalls 'StructureEditorUI' 'ProcessItemPreviewQueue' 'StructureEditorUI::InvalidateVisuals'
    $addItemPreview = $editor.Methods | Where-Object Name -eq 'AddItemPreviewCommands'
    if ($addItemPreview.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('Read', 'BuildItemVisual', 'GetPreview')
    }) { throw 'Item previews are still generated synchronously while sorting the document.' }
    $renderSurface = $editor.Methods | Where-Object Name -eq 'RenderPreviewSurface'
    if (-not ($renderSurface.Body.ExceptionHandlers | Where-Object HandlerType -eq 'Finally')) {
        throw 'Preview rendering must restore render target and GL/GUI state even after an exception.'
    }
    foreach ($method in @('DrawSelectedObject', 'DrawSelectedTerrain', 'DrawSelectedItem')) {
        Assert-MethodCalls 'StructureEditorUI' $method 'StructureEditorUI::CoordinateTextField'
    }
    Assert-MethodCalls 'StructureEditorUI' 'CoordinateTextField' 'StructureEditorUI::EditTextField'
    Assert-MethodCalls 'StructureEditorUI' 'CoordinateTextField' 'GUIUtility::set_keyboardControl'
    Assert-MethodCalls 'StructureEditorUI' 'CoordinateTextField' 'EditorCoordinateDraft::Observe'
    Assert-MethodCalls 'StructureEditorUI' 'EditTextField' 'StructureEditorUI::TextFieldAction'
    Assert-MethodCalls 'StructureEditorUI' 'EditTextField' 'GUIUtility::get_keyboardControl'
    Assert-MethodCalls 'StructureEditorUI' 'EditTextField' 'Event::get_keyCode'
    $coordinateInput = $editor.Methods | Where-Object Name -eq 'CoordinateTextField'
    if ($coordinateInput.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetNameOfFocusedControl'
    }) { throw 'Coordinate submission must not use a pre-draw focus-name lookup.' }
    'PASS Preview surface has fallback/cleanup, items use frame-budgeted jobs, and coordinate fields stage focused Enter commits.'
    Assert-MethodCalls 'StructureEditorUI' 'LabeledTextField' 'StructureEditorUI::EditorTextField'
    Assert-MethodCalls 'StructureEditorUI' 'EditorTextField' 'StructureEditorUI::EditTextField'
    Assert-MethodCalls 'StructureEditorUI' 'EditTextField' 'GUIUtility::GetControlID'
    Assert-MethodCalls 'StructureEditorUI' 'EditTextField' '(String|Object)::GetHashCode'
    Assert-MethodCalls 'StructureEditorUI' 'EditTextField' 'StructureEditorUI::SelectWholeField'
    Assert-MethodCalls 'StructureEditorUI' 'SelectWholeField' 'TextEditor::SelectAll'
    Assert-MethodCalls 'StructureEditorUI' 'HandlePreviewInput' 'GUIUtility::get_hotControl'
    'PASS Coordinate/prefab fields use namespaced control IDs and full-text selection; preview input respects captured text drags.'
    Assert-MethodCalls 'StructureEditorUI' 'OnGUI' 'StructureEditorUI::BeginEditorTextInput'
    Assert-MethodCalls 'StructureEditorUI' 'BeginEditorTextInput' 'StructureEditorUI::ReleaseEditorTextMouse'
    Assert-MethodCalls 'StructureEditorUI' 'BeginEditorTextInput' 'Event::get_rawType'
    Assert-MethodCalls 'StructureEditorUI' 'EditTextField' 'StructureEditorUI::RebindEditorTextFocus'
    Assert-MethodCalls 'StructureEditorUI' 'EditTextField' 'StructureEditorUI::TrackEditorTextInput'
    Assert-MethodCalls 'StructureEditorUI' 'RebindEditorTextFocus' 'StructureEditorUI::TransferEditorTextState'
    Assert-MethodCalls 'StructureEditorUI' 'ClearRenderCaches' 'StructureEditorUI::ClearEditorTextInput'
    $textInputCalls = @(($editor.Methods | Where-Object Name -eq 'EditTextField').Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference]
    } | ForEach-Object { $_.Operand })
    $idCalls = @($textInputCalls | Where-Object { $_.Name -eq 'GetControlID' })
    if ($idCalls.Count -ne 1 -or $idCalls[0].Parameters.Count -ne 2) {
        throw 'Text field IDs must not depend on changing layout rectangles.'
    }
    $guiCalls = @(($editor.Methods | Where-Object Name -eq 'OnGUI').Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference]
    } | ForEach-Object { $_.Operand.Name })
    if ([Array]::IndexOf($guiCalls, 'BeginEditorTextInput') -gt [Array]::IndexOf($guiCalls, 'DrawFilePanel')) {
        throw 'Text-capture recovery must run before other controls can process the click.'
    }
    'PASS Text drag ownership is released before controls, rebound with native selection, and cleaned up on both editor close paths.'
    Assert-MethodCalls 'StructureEditorUI' 'EnsureRenderOrder' 'StructureEditorUI::RebuildPreviewIndex'
    Assert-MethodCalls 'StructureEditorUI' 'DrawRecords' 'StructureEditorUI::EnsureVisiblePreview'
    Assert-MethodCalls 'StructureEditorUI' 'EnsureVisiblePreview' 'PreviewSpatialIndex::Query'
    Assert-MethodCalls 'StructureEditorUI' 'RebuildPreviewIndex' 'StructureEditorUI::PrepareSprite'
    Assert-MethodCalls 'StructureEditorUI' 'CheckPlacement' 'StructureEditorUI::CollectPlacementCandidates'
    Assert-MethodCalls 'StructureEditorUI' 'IndexPlacementRecord' 'StructureEditorUI::Profile'
    Assert-MethodCalls 'StructureEditorUI' 'FlushHistory' 'EditorDocumentHistory::Record'
    Assert-MethodCalls 'StructureEditorUI' 'RestoreEditorSnapshot' 'EditorDocumentCopy::Clone'
    Assert-MethodCalls 'StructureEditorUI' 'RebuildPreviewIndex' 'StructureEditorUI::PruneItemPreviews'
    Assert-MethodCalls 'StructureEditorUI' 'DrawFilePanel' 'StructureAlgorithms::VisibleRows'
    foreach ($closeMethod in @('Close', 'OnFrameworkSessionClosed')) {
        Assert-MethodCalls 'StructureEditorUI' $closeMethod 'StructureEditorUI::ClearRenderCaches'
    }
    $drawSprite = $editor.Methods | Where-Object Name -eq 'DrawPrefabSprite'
    $drawCalls = @($drawSprite.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
    if ($drawCalls | Where-Object { $_ -match 'Sprite::get_|Texture::get_width|Texture::get_height|StringSerializationAPI' }) {
        throw 'Per-sprite repaint is re-reading native sprite/texture geometry or serializing data.'
    }
    foreach ($method in @('FlushHistory', 'RestoreEditorSnapshot')) {
        $body = $editor.Methods | Where-Object Name -eq $method
        if ($body.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'StringSerializationAPI' }) {
            throw "$method must not use JSON serialization for undo snapshots."
        }
    }
    'PASS Editor uses visibility/placement indexes, prepared sprite data, virtual files and in-memory history; both close paths release caches.'
    foreach ($method in @('PlaceBrushObject', 'AddObject', 'DrawSelectedObject')) {
        Assert-MethodCalls 'StructureEditorUI' $method 'StructureEditorUI::PlaceEditorObject'
    }
    Assert-MethodCalls 'StructureEditorUI' 'DrawSelectedObject' 'StructureEditorUI::ChangeObjectPlacement'
    Assert-MethodCalls 'StructureEditorUI' 'DrawSelectedTerrain' 'StructureEditorUI::ChangeTerrainPlacement'
    Assert-MethodCalls 'StructureEditorUI' 'DrawSelectedTerrain' 'StructureEditorUI::PlaceEditorTerrain'
    Assert-MethodCalls 'StructureEditorUI' 'PlaceEditorObject' 'StructureEditorUI::CheckPlacement'
    Assert-MethodCalls 'StructureEditorUI' 'PlaceEditorTerrain' 'StructureEditorUI::CheckPlacement'
    Assert-MethodCalls 'StructureEditorUI' 'RemoveSlotOccupants' 'StructureEditorUI::CollectPlacementCandidates'
    Assert-MethodCalls 'StructureEditorUI' 'RemoveSlotOccupants' 'StructureEditorUI::RecordOccupies'
    Assert-MethodCalls 'StructureEditorUI' 'RemoveSlotOccupants' 'StructureEditorUI::RemoveIndexedRecords'
    Assert-MethodCalls 'StructureEditorUI' 'CheckPlacement' 'StructurePlacementPolicy::Rejection'
    Assert-MethodCalls 'StructureEditorUI' 'Save' 'StructureEditorUI::PlacementConflict'
    Assert-MethodCalls 'StructureEditorUI' 'MoveToCoordinate' 'StructurePlacementPolicy::FindConflict'
    'PASS Add, paint, move, rotate, duplicate and save paths are wired to placement checks.'
    Assert-MethodCalls 'StructureEditorUI' 'DrawPreviewPanel' 'StructureEditorUI::DrawPlacementRulesToggle'
    Assert-MethodCalls 'StructureEditorUI' 'DrawPlacementRulesToggle' 'GUILayout::Toggle'
    Assert-MethodCalls 'StructureEditorUI' 'ShouldSnap' 'StructurePlacementPolicy::ShouldSnap'
    foreach ($method in @('SnapEditorPosition','HandleItemInput','DrawNudgeControls')) {
        Assert-MethodCalls 'StructureEditorUI' $method 'StructureEditorUI::ShouldSnap'
    }
    Assert-MethodCalls 'StructureEditorUI' 'DrawNudgeControls' 'StructurePlacementPolicy::NudgeStep'
    foreach ($method in @('DrawSelectedObject','DrawSelectedItem')) {
        Assert-MethodCalls 'StructureEditorUI' $method 'StructureEditorUI::DrawNudgeControls'
    }
    $nudge = $editor.Methods | Where-Object Name -eq 'DrawNudgeControls'
    $directions = @($nudge.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' } | ForEach-Object Operand)
    foreach ($direction in @('Left','Right','Up','Down')) {
        if ($directions -notcontains $direction) { throw "Shared nudge controls are missing $direction." }
    }
    $nudgeMoves = @($nudge.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ChangeObjectPlacement'
    })
    if ($nudgeMoves.Count -ne 4) { throw 'All four item/object nudge buttons must use checked, undoable placement.' }
    if ($nudge.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'StructureItemData'
    }) { throw 'Nudging must not rewrite item payloads or apply pending item customization.' }
    'PASS Items and objects share all four nudge buttons, the same step/toggle rules and undoable movement without changing item payloads.'
    foreach ($method in @('CheckPlacement','PlaceBrushObject')) {
        $body = $editor.Methods | Where-Object Name -eq $method
        if (-not ($body.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'ignorePlacementRules' })) {
            throw "$method does not read the free-placement toggle."
        }
    }
    foreach ($method in @('Close','OnFrameworkSessionClosed')) {
        $body = $editor.Methods | Where-Object Name -eq $method
        if (-not ($body.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'ignorePlacementRules' -and $_.Previous.OpCode.Name -eq 'ldc.i4.0' })) {
            throw "$method does not restore safe placement defaults."
        }
    }
    'PASS Free-placement toggle is wired to snapping, nudging, brush/item placement and native rejections, and resets on editor close.'
    Assert-MethodCalls 'StructureEditorUI' 'DrawDetailsPanel' 'StructureEditorUI::DrawItemsTab'
    Assert-MethodCalls 'StructureEditorUI' 'HandlePreviewInput' 'StructureEditorUI::HandleItemInput'
    Assert-MethodCalls 'StructureEditorUI' 'HandleItemInput' 'StructureEditorUI::PlaceEditorObject'
    Assert-MethodCalls 'StructureEditorUI' 'HandleItemInput' 'StructureItemData::Create'
    Assert-MethodCalls 'StructureEditorUI' 'EnsureRenderOrder' 'StructureEditorUI::AddItemPreviewCommands'
    Assert-MethodCalls 'StructureEditorUI' 'SelectItemTemplate' 'LegacyStructureReader::Read'
    Assert-MethodCalls 'StructureEditorUI' 'CustomizedItem' 'ItemCustomizationRules::ValidateText'
    Assert-MethodCalls 'StructureEditorUI' 'CustomizedItem' 'ItemCustomizationRules::ValidateLetter'
    Assert-MethodCalls 'StructureEditorUI' 'DrawSelectedItem' 'StructureItemData::Write'
    Assert-MethodCalls 'StructureEditorUI' 'Close' 'StructureEditorUI::ClearItemEditor'
    Assert-MethodCalls 'StructureEditorUI' 'OnFrameworkSessionClosed' 'StructureEditorUI::ClearItemEditor'
    Assert-MethodCalls 'Plugin' 'Awake' 'StructureItemQuality::InstallTypeResolver'
    'PASS Items tab, placement, preview, customization validation and native persistence are wired; close releases editor caches.'
    $itemMethods = @($editor.Methods | Where-Object Name -in @('SelectItemTemplate','HandleItemInput','CustomizedItem'))
    $itemCodec = $assembly.MainModule.Types | Where-Object FullName -eq 'StructureHandler.StructureItemData'
    Assert-MethodCalls 'StructureItemData' 'Storage' 'StructureItemData::ReadStorage'
    Assert-MethodCalls 'StructureItemData' 'ReadItem' 'StructureItemData::ReadStorage'
    $nativeAuditCalls = @($itemCodec.Methods | Where-Object HasBody | ForEach-Object { $_.Body.Instructions } | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'VersionSafeStorage' -and $_.Operand.Name -eq 'Deserialize'
    })
    if ($nativeAuditCalls.Count -ne 0) { throw 'Offline item codec still schedules the native unread-field diagnostic.' }
    Assert-MethodCalls 'StructureItemData' 'ReadItem' 'ItemComponent::Deserialize'
    'PASS Editor envelope reads avoid native unread-field callbacks while item components still run their real deserializers.'
    $unsafeItemCalls = @(@($itemMethods) + @($itemCodec.Methods) | Where-Object HasBody | ForEach-Object { $_.Body.Instructions } |
        Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('OnUse','TryAddItem','DropItem','DeserializeSerializable','CallStarts','Instantiate') })
    if ($unsafeItemCalls.Count -ne 0) { throw 'Offline item editing invoked a gameplay placement/inventory callback.' }
    'PASS Offline item editing never instantiates world objects or invokes item-use, inventory-added or pool-Start callbacks.'
    Assert-MethodCalls 'StructureTransfer' 'ApplyLoadedImport' 'StructureTransfer::ValidatePlacement'
    Assert-MethodCalls 'StructureTransfer' 'ValidateImportFiles' 'StructureTransfer::ValidatePlacement'
    Assert-MethodCalls 'StructureTransfer' 'ValidatePlacement' 'StructurePlacementPolicy::FindConflict'
    Assert-MethodCalls 'StructureSaveState' 'ProcessLoadedTile' 'StructureTransfer::ValidateImportFiles'
    'PASS Immediate and queued imports validate exclusive slots before mutation; generic reconstruction remains separate.'
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
