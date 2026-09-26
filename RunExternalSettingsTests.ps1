param(
    [string]$AssemblyPath = '',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$BepInExPath = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx',
    [string]$ManagedAssembliesPath = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed',
    [switch]$SkipPluginBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$testCore = (Resolve-Path -LiteralPath (Join-Path $BepInExPath 'core')).Path
$testManaged = (Resolve-Path -LiteralPath $ManagedAssembliesPath).Path
$defaultAssembly = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $(if ($Configuration -eq 'Debug') { 'bin/Debug/ConfigManager.dll' } else { 'bin/ConfigManager.dll' })))
if (-not $AssemblyPath) { $AssemblyPath = $defaultAssembly }
if (-not $SkipPluginBuild)
{
    if ([IO.Path]::GetFullPath($AssemblyPath) -ne $defaultAssembly)
    {
        throw 'Use -SkipPluginBuild explicitly to test a prebuilt assembly outside the selected configuration output.'
    }

    $testGame = Split-Path -Parent ([IO.Path]::GetFullPath($BepInExPath).TrimEnd('\', '/'))
    & dotnet build (Join-Path $PSScriptRoot 'ConfigurationManager.csproj') -c $Configuration -p:DeployToGame=true -p:EnableReleasePackaging=false "-p:ValheimGamePath=$testGame" "-p:BepInExPath=$BepInExPath" "-p:ManagedAssembliesPath=$testManaged" -verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
}
$testAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ConfigManager-external-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $testRoot 'BepInEx/config')) | Out-Null
& dotnet msbuild (Join-Path $PSScriptRoot 'HiddenSettingsParser.Tests.csproj') -t:Build "-p:Configuration=$Configuration" -verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw 'Test harness build failed.' }
# Keep the .NET Framework loader setting local to this isolated test process.
# Download-zone game assemblies may otherwise fail to load with 0x80131515.
$testExe = Join-Path $testRoot 'HiddenSettingsParser.Tests.exe'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "obj/HiddenSettingsParser.Tests/bin/$Configuration/HiddenSettingsParser.Tests.exe") -Destination $testExe
[IO.File]::WriteAllText(($testExe + '.config'), '<configuration><runtime><loadFromRemoteSources enabled="true" /></runtime></configuration>')
& $testExe $testAssembly $testCore $testManaged $testRoot
if ($LASTEXITCODE -ne 0) { throw "External setting tests failed. Artifacts: $testRoot" }
Write-Host "External setting tests passed. Isolated BepInEx state: $testRoot"
