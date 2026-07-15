[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,

    [switch]$CreateNexusPackage
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path.TrimEnd('\')
$assemblyFullPath = (Resolve-Path -LiteralPath $AssemblyPath).Path

function Get-ProjectPath
{
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $fullPath = [IO.Path]::GetFullPath((Join-Path $projectRootPath $RelativePath))
    if (-not $fullPath.StartsWith($projectRootPath + '\', [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Path escaped the project root: $fullPath"
    }

    return $fullPath
}

if (-not $assemblyFullPath.StartsWith($projectRootPath + '\', [StringComparison]::OrdinalIgnoreCase))
{
    throw "Assembly is outside the project root: $assemblyFullPath"
}

$rootReadme = Get-ProjectPath "README.md"
$thunderstoreDirectory = Get-ProjectPath "Thunderstore"
$thunderstoreReadme = Get-ProjectPath "Thunderstore\README.md"
$manifestPath = Get-ProjectPath "Thunderstore\manifest.json"
$changeLogPath = Get-ProjectPath "Thunderstore\CHANGELOG.md"
$iconPath = Get-ProjectPath "Thunderstore\icon.png"

foreach ($requiredFile in @($assemblyFullPath, $rootReadme, $manifestPath, $changeLogPath, $iconPath))
{
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf))
    {
        throw "Required release file was not found: $requiredFile"
    }
}

if (Test-Path -LiteralPath $thunderstoreReadme)
{
    throw "Thunderstore\README.md must not exist. The root README.md is the only README source."
}

$assemblyIdentity = [Reflection.AssemblyName]::GetAssemblyName($assemblyFullPath)
$assemblyVersion = $assemblyIdentity.Version
if ($assemblyVersion.Build -lt 0 -or $assemblyVersion.Revision -ne 0)
{
    throw "AssemblyVersion must be major.minor.patch with a zero revision: $assemblyVersion"
}

$packageVersion = "{0}.{1}.{2}" -f $assemblyVersion.Major, $assemblyVersion.Minor, $assemblyVersion.Build
$packageName = "{0}_v{1}" -f $assemblyIdentity.Name, $packageVersion
$dllName = [IO.Path]::GetFileName($assemblyFullPath)

$manifestContent = [IO.File]::ReadAllText($manifestPath)
$versionPattern = '("version_number"\s*:\s*")[^"]*(")'
$versionMatches = [Text.RegularExpressions.Regex]::Matches($manifestContent, $versionPattern)
if ($versionMatches.Count -ne 1)
{
    throw "manifest.json must contain exactly one version_number property."
}

$updatedManifest = [Text.RegularExpressions.Regex]::Replace(
    $manifestContent,
    $versionPattern,
    { param($match) $match.Groups[1].Value + $packageVersion + $match.Groups[2].Value },
    1)

$parsedManifest = $updatedManifest | ConvertFrom-Json
if ($parsedManifest.version_number -ne $packageVersion)
{
    throw "Failed to update manifest.json to $packageVersion."
}

if ($updatedManifest -ne $manifestContent)
{
    $temporaryManifest = $manifestPath + ".tmp"
    try
    {
        $utf8WithoutBom = New-Object Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($temporaryManifest, $updatedManifest, $utf8WithoutBom)
        Move-Item -LiteralPath $temporaryManifest -Destination $manifestPath -Force
    }
    finally
    {
        if (Test-Path -LiteralPath $temporaryManifest)
        {
            Remove-Item -LiteralPath $temporaryManifest -Force
        }
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-VerifiedZip
{
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$DestinationFile,
        [Parameter(Mandatory = $true)][string[]]$ExpectedEntries
    )

    if (Test-Path -LiteralPath $DestinationFile)
    {
        Remove-Item -LiteralPath $DestinationFile -Force
    }

    [IO.Compression.ZipFile]::CreateFromDirectory(
        $SourceDirectory,
        $DestinationFile,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)

    $archive = [IO.Compression.ZipFile]::OpenRead($DestinationFile)
    try
    {
        $actualEntries = @($archive.Entries | ForEach-Object { $_.FullName })
        $differences = @(Compare-Object -ReferenceObject ($ExpectedEntries | Sort-Object) -DifferenceObject ($actualEntries | Sort-Object))
        if ($actualEntries.Count -ne $ExpectedEntries.Count -or $differences.Count -ne 0)
        {
            throw "Unexpected entries in $DestinationFile`: $($actualEntries -join ', ')"
        }
    }
    finally
    {
        $archive.Dispose()
    }
}

$temporaryParent = Get-ProjectPath "obj\ReleasePackage"
$temporaryRoot = Join-Path $temporaryParent $packageName
if (Test-Path -LiteralPath $temporaryRoot)
{
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}

try
{
    $thunderstoreStage = Join-Path $temporaryRoot "Thunderstore"
    New-Item -ItemType Directory -Path $thunderstoreStage -Force | Out-Null

    $thunderstoreFiles = @(
        @{ Source = $assemblyFullPath; Name = $dllName },
        @{ Source = $rootReadme; Name = "README.md" },
        @{ Source = $changeLogPath; Name = "CHANGELOG.md" },
        @{ Source = $manifestPath; Name = "manifest.json" },
        @{ Source = $iconPath; Name = "icon.png" }
    )

    foreach ($file in $thunderstoreFiles)
    {
        Copy-Item -LiteralPath $file.Source -Destination (Join-Path $thunderstoreStage $file.Name) -Force
    }

    $thunderstoreZip = Join-Path $thunderstoreDirectory ($packageName + ".zip")
    New-VerifiedZip -SourceDirectory $thunderstoreStage -DestinationFile $thunderstoreZip -ExpectedEntries @(
        $dllName,
        "README.md",
        "CHANGELOG.md",
        "manifest.json",
        "icon.png"
    )

    Write-Output "Package version $packageVersion (AssemblyVersion $assemblyVersion)"
    Write-Output "Thunderstore package $thunderstoreZip"

    if ($CreateNexusPackage)
    {
        $nexusDirectory = Get-ProjectPath "Nexus"
        New-Item -ItemType Directory -Path $nexusDirectory -Force | Out-Null

        $nexusStage = Join-Path $temporaryRoot "Nexus"
        New-Item -ItemType Directory -Path $nexusStage -Force | Out-Null
        Copy-Item -LiteralPath $assemblyFullPath -Destination (Join-Path $nexusStage $dllName) -Force

        $nexusZip = Join-Path $nexusDirectory ($packageName + ".zip")
        New-VerifiedZip -SourceDirectory $nexusStage -DestinationFile $nexusZip -ExpectedEntries @($dllName)
        Write-Output "Nexus package $nexusZip"
    }
}
finally
{
    if (Test-Path -LiteralPath $temporaryRoot)
    {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }

    if ((Test-Path -LiteralPath $temporaryParent) -and (Get-ChildItem -LiteralPath $temporaryParent -Force | Measure-Object).Count -eq 0)
    {
        Remove-Item -LiteralPath $temporaryParent -Force
    }
}

if (Test-Path -LiteralPath $thunderstoreReadme)
{
    throw "Thunderstore\README.md was unexpectedly created."
}
