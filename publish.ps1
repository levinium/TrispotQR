<#
.SYNOPSIS
    Builds TrispotQR for distribution.

.DESCRIPTION
    Runs the test suite, then produces two builds in dist\:

      dist\TrispotQR.exe                      self-contained, needs nothing installed
      dist\framework-dependent\TrispotQR.exe  small, needs the .NET 10 Desktop Runtime

    The self-contained build is the one to copy onto another machine or a shared drive:
    it carries the .NET runtime inside it, so the target machine needs nothing installed.
    That is what makes it roughly 67 MB, since WPF and Skia's native library cannot be
    trimmed. The framework-dependent build carries the same untrimmable native Skia library
    but not the .NET runtime, which is the whole of the roughly 13 MB it saves.

.PARAMETER SkipTests
    Publishes without running the tests first. Use only when the suite has just passed.
#>

[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\TrispotQR.App\TrispotQR.App.csproj'
$dist = Join-Path $root 'dist'

# Read straight out of the csproj, which is the single source of truth. Printed at the end
# so whatever gets handed over is always identifiable. See CHANGELOG.md to release.
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ }
if (-not $version) { throw 'No <Version> found in the project file.' }
Write-Host "Building Trispot QR v$version" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    dotnet test $root -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Tests failed. Nothing was published.'
    }
}

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}

Write-Host 'Publishing self-contained build...' -ForegroundColor Cyan
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $dist `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }

Write-Host 'Publishing framework-dependent build...' -ForegroundColor Cyan
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -o (Join-Path $dist 'framework-dependent') `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'Framework-dependent publish failed.' }

Write-Host ''
Write-Host "Done. Published Trispot QR v$version" -ForegroundColor Green

Get-ChildItem $dist -Filter TrispotQR.exe -Recurse |
    Select-Object @{ Name = 'Build'; Expression = { if ($_.Directory.Name -eq 'dist') { 'self-contained' } else { 'framework-dependent' } } },
                  @{ Name = 'Size';  Expression = { '{0:N1} MB' -f ($_.Length / 1MB) } },
                  FullName |
    Format-Table -AutoSize
