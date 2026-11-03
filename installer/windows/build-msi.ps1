<#
.SYNOPSIS
    Builds the per-user MSI from a published win-x64 build.

.DESCRIPTION
    Reads VERSION, PUBLISH_DIR, OUTPUT_DIR and RUNTIME from the environment, which is what the release
    workflow sets, and also accepts them as parameters for local use. WiX is installed as a local .NET
    tool on first use, so the only prerequisite is the .NET SDK the project is built with.

    The package needs nothing but the single-file executable.

.EXAMPLE
    ./installer/windows/build-msi.ps1 -Version 0.1.0 -SourceDir publish/win-x64 -OutputDir release
#>
[CmdletBinding()]
param(
    [string]$Version = $Env:VERSION,
    [string]$SourceDir = $Env:PUBLISH_DIR,
    [string]$OutputDir = $Env:OUTPUT_DIR,
    [string]$Runtime = $Env:RUNTIME
)

$ErrorActionPreference = "Stop"

if (-not $Version) { $Version = (dotnet msbuild (Join-Path $PSScriptRoot "..\..\ClipboardSync\ClipboardSync.csproj") -getProperty:Version).Trim() }
if (-not $SourceDir) { $SourceDir = "publish/win-x64" }
if (-not $OutputDir) { $OutputDir = "release" }
if (-not $Runtime) { $Runtime = "win-x64" }

$source = (Resolve-Path $SourceDir).Path
$icon = (Resolve-Path (Join-Path $PSScriptRoot "..\..\ClipboardSync\Resources\icon.ico")).Path
$wxs = Join-Path $PSScriptRoot "ClipboardSync.wxs"

if (-not (Test-Path (Join-Path $source "ClipboardSync.exe"))) {
    throw "No ClipboardSync.exe in $source; publish for win-x64 first."
}

# WiX ships as a .NET tool; keep it local to this repository instead of polluting the machine.
# Version 5 is the newest release that does not require agreeing to the WiX maintenance fee.
$toolPath = Join-Path $PSScriptRoot ".wix"
if (-not (Test-Path (Join-Path $toolPath "wix.exe"))) {
    dotnet tool install wix --tool-path $toolPath --version "5.*" | Out-Null
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$output = Join-Path (Resolve-Path $OutputDir).Path "ClipboardSync-$Version-$Runtime.msi"

& (Join-Path $toolPath "wix.exe") build `
    -arch x64 `
    -d "Version=$Version" `
    -d "SourceDir=$source" `
    -d "IconPath=$icon" `
    -o $output `
    $wxs

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $output)) {
    throw "WiX failed to build $output"
}

Get-Item $output
