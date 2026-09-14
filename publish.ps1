<#
.SYNOPSIS
    Builds TrispotQR.exe for distribution, with its checksum.

.DESCRIPTION
    Runs every test suite, then produces exactly two files in dist\:

      dist\TrispotQR.exe          self-contained single file, needs nothing installed
      dist\TrispotQR.exe.sha256   its SHA-256, in sha256sum format

    These are the files a release publishes, and the same two the app's updater downloads: it
    refuses an exe whose hash does not match the checksum published beside it. The release
    workflow runs this script, so a desk build and a published one are made the same way.

    The framework-dependent build is gone as of 1.2.0. It needed native libraries beside the
    exe, and an updater that replaces one file cannot safely update a folder of them.

.PARAMETER SkipTests
    Publishes without running the tests first. Use only when the suite has just passed.

.PARAMETER UpdateFeedUrl
    Where the built app looks for newer releases. Defaults to this project's own. Pass a fork's
    feed, a local test server's address, or "" to build an app that never checks.

.PARAMETER UpdatePageUrl
    Where the app sends people to download by hand. Only meaningful with -UpdateFeedUrl.
#>

[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$UpdateFeedUrl,
    [string]$UpdatePageUrl,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\TrispotQR.Desktop\TrispotQR.Desktop.csproj'
$dist = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $root 'dist' }

# Where-Object because the file has several PropertyGroups and only one carries a Version.
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ }
if (-not $version) { throw 'No <Version> found in the project file.' }
Write-Host "Building Trispot QR v$version" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    dotnet test (Join-Path $root 'TrispotQR.slnx') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Nothing was published.' }
}

# PSBoundParameters rather than truthiness: "" is a meaning here (never check), and a plain
# `if ($UpdateFeedUrl)` cannot tell it apart from not passing the parameter at all. [string[]]
# and the leading commas keep a one-element array from unrolling into a string that splats one
# character at a time.
[string[]] $updates = @()
if ($PSBoundParameters.ContainsKey('UpdateFeedUrl')) {
    $updates += , "-p:UpdateFeedUrl=$UpdateFeedUrl"
    $page = if ($PSBoundParameters.ContainsKey('UpdatePageUrl')) { $UpdatePageUrl } else { $UpdateFeedUrl }
    $updates += , "-p:UpdatePageUrl=$page"
    Write-Host "  Update feed: $(if ($UpdateFeedUrl) { $UpdateFeedUrl } else { 'none, this build never checks' })"
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

Write-Host 'Publishing...' -ForegroundColor Cyan
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $dist `
    --nologo @updates
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $dist 'TrispotQR.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe but it is not there." }

# Anything else beside the exe means the single file is not single, and the updater, which
# replaces only the exe, would leave those files stale.
$strays = Get-ChildItem $dist -File | Where-Object { $_.Name -ne 'TrispotQR.exe' }
if ($strays) { throw "Unexpected files beside the exe: $($strays.Name -join ', ')" }

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  TrispotQR.exe" | Out-File -FilePath (Join-Path $dist 'TrispotQR.exe.sha256') -Encoding ascii -NoNewline

Write-Host ''
Write-Host "Done. Published Trispot QR v$version" -ForegroundColor Green
Write-Host ("  {0}  {1:N1} MB" -f $exe, ((Get-Item $exe).Length / 1MB))
Write-Host "  SHA256 $hash"
