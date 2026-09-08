param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot 'bin/ConfigManager.dll'),
    [string]$BepInExPath = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx',
    [string]$ManagedAssembliesPath = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed',
    [switch]$SkipPluginBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$testCore = (Resolve-Path -LiteralPath (Join-Path $BepInExPath 'core')).Path
$testManaged = (Resolve-Path -LiteralPath $ManagedAssembliesPath).Path
if (-not $SkipPluginBuild)
{
    $defaultAssembly = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'bin/ConfigManager.dll'))
    if ([IO.Path]::GetFullPath($AssemblyPath) -ne $defaultAssembly)
    {
        throw 'Use -SkipPluginBuild explicitly to test a prebuilt assembly outside the default Release output.'
    }

    $testGame = Split-Path -Parent ([IO.Path]::GetFullPath($BepInExPath).TrimEnd('\', '/'))
    & dotnet msbuild (Join-Path $PSScriptRoot 'ConfigurationManager.csproj') -t:Build -p:Configuration=Release -p:EnableReleasePackaging=false "-p:ValheimGamePath=$testGame" "-p:BepInExPath=$BepInExPath" "-p:ManagedAssembliesPath=$testManaged" -verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
}
$testAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ConfigManager-external-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $testRoot 'BepInEx/config')) | Out-Null
& dotnet msbuild (Join-Path $PSScriptRoot 'HiddenSettingsParser.Tests.csproj') -t:Build -p:Configuration=Release -verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw 'Test harness build failed.' }
# Keep the .NET Framework loader setting local to this isolated test process.
# Download-zone game assemblies may otherwise fail to load with 0x80131515.
$testExe = Join-Path $testRoot 'HiddenSettingsParser.Tests.exe'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'obj/HiddenSettingsParser.Tests/bin/Release/HiddenSettingsParser.Tests.exe') -Destination $testExe
[IO.File]::WriteAllText(($testExe + '.config'), '<configuration><runtime><loadFromRemoteSources enabled="true" /></runtime></configuration>')
& $testExe $testAssembly $testCore $testManaged $testRoot
if ($LASTEXITCODE -ne 0) { throw "External setting tests failed. Artifacts: $testRoot" }
Write-Host "External setting tests passed. Isolated BepInEx state: $testRoot"
