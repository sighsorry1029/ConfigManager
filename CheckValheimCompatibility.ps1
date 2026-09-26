#requires -Version 7.0
param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot 'bin/Debug/ConfigManager.dll'),
    [string]$BepInExPath = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx',
    [string]$ManagedAssembliesPath = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Inspect original metadata without loading game types into the .NET Framework test host.
# Valheim 1.0 interfaces can contain default methods that this host cannot execute.
Add-Type -Path (Join-Path $BepInExPath 'core/Mono.Cecil.dll')
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Resolve-Path $ManagedAssembliesPath).Path)
$resolver.AddSearchDirectory((Join-Path $BepInExPath 'core'))
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $AssemblyPath).Path, $parameters)
$targets = [Collections.Generic.List[string]]::new()
$fieldCount = 0
function Assert-Method($owner, [string]$name, $argumentTypes) {
    $type = $owner.Resolve()
    while ($null -ne $type) {
        $matches = @($type.Methods | Where-Object Name -CEQ $name)
        if ($argumentTypes.Count -gt 0) {
            $signature = ($argumentTypes | ForEach-Object FullName) -join ','
            $matches = @($matches | Where-Object { (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -ceq $signature })
        }
        if ($matches.Count -gt 0) {
            if ($matches.Count -ne 1) { throw "Ambiguous target: $($owner.FullName).$name" }
            $targets.Add("$($owner.FullName).$name")
            return
        }
        $type = if ($type.BaseType) { $type.BaseType.Resolve() } else { $null }
    }
    throw "Missing target: $($owner.FullName).$name"
}
try {
    foreach ($type in $assembly.MainModule.GetTypes()) {
        foreach ($attribute in $type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' }) {
            if ($attribute.ConstructorArguments.Count -eq 2) {
                Assert-Method $attribute.ConstructorArguments[0].Value $attribute.ConstructorArguments[1].Value @()
            } elseif ($attribute.ConstructorArguments.Count -ne 0) {
                throw "Uncovered Harmony annotation on $($type.FullName)"
            }
        }
        foreach ($method in $type.Methods | Where-Object HasBody) {
            $tokens = [Collections.Generic.List[object]]::new()
            $name = $null
            foreach ($instruction in $method.Body.Instructions) {
                if ($instruction.OpCode.Code -eq 'Ldtoken' -and $instruction.Operand -is [Mono.Cecil.TypeReference]) { $tokens.Add($instruction.Operand) }
                if ($instruction.OpCode.Code -eq 'Ldstr') { $name = [string]$instruction.Operand }
                if ($instruction.OpCode.Code -ne 'Call' -or $instruction.Operand.DeclaringType.FullName -ne 'HarmonyLib.AccessTools') { continue }
                if ($instruction.Operand.Name -notin @('Method', 'Field')) { $tokens.Clear(); $name = $null; continue }
                if ($tokens.Count -eq 0 -or -not $name) { throw "Uncovered AccessTools expression in $($method.FullName)" }
                $owner = $tokens[0]
                if ($instruction.Operand.Name -eq 'Method') {
                    Assert-Method $owner $name @($tokens | Select-Object -Skip 1)
                } else {
                    $field = @($owner.Resolve().Fields | Where-Object Name -CEQ $name)
                    if ($field.Count -ne 1) { throw "Missing/ambiguous field: $($owner.FullName).$name" }
                    $fieldCount++
                }
                $tokens.Clear()
                $name = $null
            }
        }
    }
    foreach ($name in @('InventoryGrid.OnLeftDown', 'InventoryGrid.OnLeftClick', 'InventoryGrid.OnRightDown', 'InventoryGui.OnSelectedItem', 'InventoryGui.OnRightClickItem', 'InventoryGui.OnDropOutside')) {
        if (-not $targets.Contains($name)) { throw "Missing inventory guard: $name" }
    }
    foreach ($name in @('InventoryGrid.OnLeftRelease', 'InventoryGrid.OnDragEnd', 'InventoryGrid.OnReleasedOn', 'InventoryGui.OnReleasedItem')) {
        if ($targets.Contains($name)) { throw "Release cleanup must not be blocked: $name" }
    }
    $game = $resolver.Resolve([Mono.Cecil.AssemblyNameReference]::new('assembly_valheim', [Version]'0.0.0.0'))
    $closeState = $game.MainModule.GetType('Menu').NestedTypes | Where-Object Name -eq CloseMenuState
    foreach ($name in @('SettingsOpen', 'CanBeClosed')) {
        if (-not ($closeState.Fields | Where-Object Name -CEQ $name)) { throw "Missing Menu.CloseMenuState.$name" }
    }
    if ($targets.Count -ne 33 -or $fieldCount -ne 6) { throw "Review changed contract coverage: $($targets.Count) targets / $fieldCount fields" }
    Write-Host "PASS original metadata: $($targets.Count) Harmony targets, $fieldCount reflection fields, inventory release boundaries and menu state names."
} finally {
    $assembly.Dispose()
    $resolver.Dispose()
}
