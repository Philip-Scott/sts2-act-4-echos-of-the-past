<#
.SYNOPSIS
    Builds The Architect mod with a single command.
.DESCRIPTION
    The mod compiles against sts2.dll and 0Harmony.dll, which ship with the game and cannot be
    redistributed. Point the script at a Slay the Spire 2 install (or at a folder containing those
    assemblies) with the parameters below; by default the paths are auto-detected by
    Sts2PathDiscovery.props.
.EXAMPLE
    ./scripts/build.ps1
.EXAMPLE
    ./scripts/build.ps1 -Configuration Release -Publish -Godot C:/megadot/MegaDot_v4.5.1-stable_mono_win64.exe
#>
[CmdletBinding()]
param(
    [string]$Configuration = $(if ($env:CONFIGURATION) { $env:CONFIGURATION } else { 'Debug' }),
    [string]$Sts2Path = $env:STS2_PATH,
    [string]$Sts2DataDir = $env:STS2_DATA_DIR,
    [string]$ModsPath = $env:MODS_PATH,
    [string]$Godot = $env:GODOT_BIN,
    [switch]$Publish,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExtraArgs
)

$ErrorActionPreference = 'Stop'

$rootDir = Split-Path -Parent $PSScriptRoot
$project = Join-Path $rootDir 'TheArchitect.csproj'

$msbuildArgs = @()
if ($Sts2Path) { $msbuildArgs += "-p:Sts2Path=$Sts2Path" }
if ($Sts2DataDir) { $msbuildArgs += "-p:Sts2DataDir=$Sts2DataDir" }
if ($Godot) { $msbuildArgs += "-p:GodotPath=$Godot" }

if ($ModsPath) {
    $null = New-Item -ItemType Directory -Force -Path $ModsPath
    # The build targets append the project name to this path, so it must end with a separator.
    $resolved = (Resolve-Path $ModsPath).Path.TrimEnd('/', '\') + '/'
    $msbuildArgs += "-p:ModsPath=$resolved"
}

$verb = if ($Publish) { 'publish' } else { 'build' }
Write-Host "Running dotnet $verb on $project ($Configuration)"

& dotnet $verb $project -c $Configuration @msbuildArgs @ExtraArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
