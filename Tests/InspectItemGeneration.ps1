$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path (Get-Location) 'BepInEx/core/Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Get-Location) 'Silverpine_Data/Managed/Assembly-CSharp.dll'))
function Read-Type($type) {
    foreach ($method in $type.Methods) {
        if (!$method.HasBody) { continue }
        foreach ($instruction in $method.Body.Instructions) {
            if ($instruction.OpCode.Name -eq 'ldstr') { "$($type.FullName).$($method.Name): $($instruction.Operand)" }
        }
    }
    foreach ($child in $type.NestedTypes) { Read-Type $child }
}
try {
    $game.MainModule.Types | Where-Object Name -in @('CustomItemGenerator','NPCFunction_GiveItem','CustomCraftingUIData') | ForEach-Object { Read-Type $_ }
} finally { $game.Dispose() }
