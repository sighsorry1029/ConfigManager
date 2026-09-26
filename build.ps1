param(
    [string]$ValheimGamePath = "",
    [switch]$SkipPackaging
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = $PSScriptRoot
$msbuildCommand = Get-Command msbuild.exe -ErrorAction SilentlyContinue
$msbuild = if ($msbuildCommand) { $msbuildCommand.Source } else { "" }
if (-not $msbuild)
{
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere)
    {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    }
}

if (-not $msbuild -or -not (Test-Path $msbuild))
{
    throw "MSBuild.exe was not found. Install Visual Studio Build Tools with the .NET Framework 4.8 targeting pack."
}

$commonArguments = @(
    "/m",
    "/t:Rebuild",
    "/p:Configuration=Release",
    "/p:Platform=AnyCPU",
    "/v:minimal"
)

& $msbuild (Join-Path $projectRoot "HiddenSettingsParser.Tests.csproj") @commonArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $projectRoot "obj\HiddenSettingsParser.Tests\bin\Release\HiddenSettingsParser.Tests.exe")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$projectArguments = @($commonArguments)
$projectArguments += "/p:EnableReleasePackaging=false"
if ($ValheimGamePath)
{
    $projectArguments += "/p:ValheimGamePath=$ValheimGamePath"
}

& $msbuild (Join-Path $projectRoot "ConfigurationManager.csproj") @projectArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $projectRoot "bin\ConfigManager.dll"
if (-not (Test-Path $dll))
{
    throw "ConfigManager.dll was not produced."
}

$privateDlls = @(Get-ChildItem (Split-Path $dll) -Filter *.dll -File | Where-Object Name -ne "ConfigManager.dll")
if ($privateDlls.Count -ne 0)
{
    throw "Unexpected private DLLs in Release output: $($privateDlls.Name -join ', ')"
}

if (-not $ValheimGamePath)
{
    $ValheimGamePath = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
        "C:\Program Files\Steam\steamapps\common\Valheim"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $ValheimGamePath)
{
    throw "Valheim was not found. Pass -ValheimGamePath explicitly."
}

& (Join-Path $projectRoot "RunExternalSettingsTests.ps1") -Configuration Release -AssemblyPath $dll -BepInExPath (Join-Path $ValheimGamePath "BepInEx") -ManagedAssembliesPath (Join-Path $ValheimGamePath "valheim_Data\Managed") -SkipPluginBuild

$cecilPath = Join-Path $ValheimGamePath "BepInEx\core\Mono.Cecil.dll"
if (-not (Test-Path $cecilPath))
{
    throw "Mono.Cecil.dll was not found under the selected Valheim installation."
}

Add-Type -Path $cecilPath
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try
{
    $denyPattern = "^(ConditionalConfigSync|ServerSync|Newtonsoft(\.|$)|YamlDotNet(\.|$)|ILRepack)"
    $forbiddenReferences = @($assembly.MainModule.AssemblyReferences | Where-Object Name -match $denyPattern)
    $forbiddenTypes = @($assembly.MainModule.GetTypeReferences() | Where-Object { $_.Namespace -match $denyPattern -or $_.FullName -match $denyPattern })
    $forbiddenDefinitions = @($assembly.MainModule.GetTypes() | Where-Object { $_.Namespace -match $denyPattern -or $_.FullName -match $denyPattern })

    if ($forbiddenReferences.Count -ne 0 -or $forbiddenTypes.Count -ne 0 -or $forbiddenDefinitions.Count -ne 0)
    {
        throw "A removed dependency is still present in ConfigManager.dll."
    }

    $managedPath = Join-Path $ValheimGamePath "valheim_Data\Managed"
    $gameAssemblies = @()
    try
    {
        foreach ($gameAssemblyName in @("assembly_valheim.dll", "assembly_guiutils.dll", "assembly_utils.dll"))
        {
            $gameAssemblyPath = Join-Path $managedPath $gameAssemblyName
            if (-not (Test-Path $gameAssemblyPath))
            {
                throw "$gameAssemblyName was not found under $managedPath."
            }

            $gameAssemblies += [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gameAssemblyPath)
        }

        $gameTypes = @{}
        foreach ($gameAssembly in $gameAssemblies)
        {
            foreach ($gameType in $gameAssembly.MainModule.GetTypes())
            {
                $gameTypes[$gameType.FullName] = $gameType
            }
        }

        $nonPublicGameMembers = @()
        foreach ($ownerType in $assembly.MainModule.GetTypes())
        {
            foreach ($method in $ownerType.Methods)
            {
                if (-not $method.HasBody) { continue }

                foreach ($instruction in $method.Body.Instructions)
                {
                    if ($instruction.Operand -is [Mono.Cecil.FieldReference])
                    {
                        $fieldReference = $instruction.Operand
                        $declaringType = $gameTypes[$fieldReference.DeclaringType.FullName]
                        if ($declaringType)
                        {
                            $fieldDefinition = $declaringType.Fields | Where-Object Name -eq $fieldReference.Name | Select-Object -First 1
                            if ($fieldDefinition -and -not $fieldDefinition.IsPublic)
                            {
                                $nonPublicGameMembers += "field $($fieldReference.FullName)"
                            }
                        }
                    }
                    elseif ($instruction.Operand -is [Mono.Cecil.MethodReference])
                    {
                        $methodReference = $instruction.Operand
                        $declaringType = $gameTypes[$methodReference.DeclaringType.FullName]
                        if ($declaringType)
                        {
                            $parameterSignature = ($methodReference.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join "|"
                            $methodDefinition = $declaringType.Methods | Where-Object {
                                $_.Name -eq $methodReference.Name -and
                                (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join "|") -eq $parameterSignature
                            } | Select-Object -First 1

                            if ($methodDefinition -and -not $methodDefinition.IsPublic)
                            {
                                $nonPublicGameMembers += "method $($methodReference.FullName)"
                            }
                        }
                    }
                }
            }
        }

        foreach ($typeReference in $assembly.MainModule.GetTypeReferences())
        {
            $typeDefinition = $gameTypes[$typeReference.FullName]
            if ($typeDefinition -and -not ($typeDefinition.IsPublic -or $typeDefinition.IsNestedPublic))
            {
                $nonPublicGameMembers += "type $($typeReference.FullName)"
            }
        }

        $nonPublicGameMembers = @($nonPublicGameMembers | Sort-Object -Unique)
        if ($nonPublicGameMembers.Count -ne 0)
        {
            throw "ConfigManager directly accesses non-public Valheim API:`n$($nonPublicGameMembers -join "`n")"
        }
    }
    finally
    {
        foreach ($gameAssembly in $gameAssemblies)
        {
            $gameAssembly.Dispose()
        }
    }
}
finally
{
    $assembly.Dispose()
}

$hash = (Get-FileHash $dll -Algorithm SHA256).Hash
Write-Output "Built $dll"
Write-Output "SHA256 $hash"

if (-not $SkipPackaging)
{
    & (Join-Path $projectRoot "PackageRelease.ps1") -ProjectRoot $projectRoot -AssemblyPath $dll -CreateNexusPackage
}
