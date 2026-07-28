param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [Parameter(Mandatory)]
    [long] $ExpectedRevision,

    [Parameter(Mandatory)]
    [ValidatePattern("^[0-9a-fA-F]{64}$")]
    [string] $ExpectedSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-StreamSha256Hex
{
    param(
        [Parameter(Mandatory)]
        [IO.Stream] $Stream
    )

    $algorithm = [Security.Cryptography.SHA256]::Create()

    try
    {
        $hash = $algorithm.ComputeHash($Stream)
        return ([BitConverter]::ToString($hash)).Replace("-", "").ToLowerInvariant()
    }
    finally
    {
        $algorithm.Dispose()
    }
}

$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$bundleDirectory = Join-Path $repositoryRoot "Source\Net7ClientManager\BundledData"
$resolvedPackagePath = (Resolve-Path $PackagePath).Path
$normalizedExpectedSha256 = $ExpectedSha256.ToLowerInvariant()
$actualSha256 = ((Get-FileHash $resolvedPackagePath -Algorithm SHA256).Hash).ToLowerInvariant()

if ($actualSha256 -cne $normalizedExpectedSha256)
{
    throw "Package SHA-256 '$actualSha256' does not match '$normalizedExpectedSha256'."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedPackagePath)

try
{
    if ($archive.Entries.Count -ne 2)
    {
        throw "The navigation package must contain exactly two entries."
    }

    $manifestEntry = $archive.GetEntry("net7forge.json")
    $navigationEntry = $archive.GetEntry("navigation.json")

    if ($null -eq $manifestEntry -or
        $null -eq $navigationEntry -or
        $manifestEntry.FullName -cne $manifestEntry.Name -or
        $navigationEntry.FullName -cne $navigationEntry.Name)
    {
        throw "The navigation package has missing or unsafe entries."
    }

    $manifestStream = $manifestEntry.Open()
    $manifestReader = [IO.StreamReader]::new(
        $manifestStream,
        [Text.UTF8Encoding]::new($false),
        $true)

    try
    {
        $manifest = $manifestReader.ReadToEnd() | ConvertFrom-Json
    }
    finally
    {
        $manifestReader.Dispose()
        $manifestStream.Dispose()
    }

    if ($manifest.packageFormatVersion -ne 1 -or
        $manifest.dataSchemaVersion -ne 1 -or
        $manifest.dataRevision -ne $ExpectedRevision -or
        $manifest.component -cne "navigation" -or
        $manifest.mode -cne "full" -or
        $manifest.navigationEntry -cne "navigation.json" -or
        $null -ne $manifest.baseRevision -or
        $manifest.navigationSha256 -cne $manifest.resultNavigationSha256)
    {
        throw "The navigation package manifest does not match the requested bundled baseline."
    }

    $navigationStream = $navigationEntry.Open()

    try
    {
        $navigationSha256 = Get-StreamSha256Hex $navigationStream
    }
    finally
    {
        $navigationStream.Dispose()
    }

    if ($navigationSha256 -cne $manifest.navigationSha256)
    {
        throw "The navigation payload SHA-256 does not match its manifest."
    }
}
finally
{
    $archive.Dispose()
}

New-Item $bundleDirectory -ItemType Directory -Force | Out-Null
$targetPath = Join-Path $bundleDirectory ($normalizedExpectedSha256 + ".n7data")
$temporaryPath = Join-Path $bundleDirectory (
    ".navigation-" + [Guid]::NewGuid().ToString("N") + ".tmp")

try
{
    Copy-Item $resolvedPackagePath $temporaryPath

    if (((Get-FileHash $temporaryPath -Algorithm SHA256).Hash).ToLowerInvariant() -cne
        $normalizedExpectedSha256)
    {
        throw "The package changed while it was being copied."
    }

    if (Test-Path $targetPath -PathType Leaf)
    {
        $targetSha256 = ((Get-FileHash $targetPath -Algorithm SHA256).Hash).ToLowerInvariant()

        if ($targetSha256 -cne $normalizedExpectedSha256)
        {
            throw "The existing bundled package at '$targetPath' is corrupt."
        }
    }
    else
    {
        Move-Item $temporaryPath $targetPath
    }

    foreach ($existingPackage in Get-ChildItem $bundleDirectory -Filter "*.n7data" -File)
    {
        if ($existingPackage.FullName -cne $targetPath)
        {
            Remove-Item $existingPackage.FullName -Force
        }
    }
}
finally
{
    if (Test-Path $temporaryPath)
    {
        Remove-Item $temporaryPath -Force
    }
}

Write-Host ""
Write-Host "Bundled navigation baseline refreshed" -ForegroundColor Green
Write-Host "Revision: $ExpectedRevision"
Write-Host "SHA-256: $normalizedExpectedSha256"
Write-Host "Package:  $targetPath"
