<#
.SYNOPSIS
    Builds STS2AgentMcp.dll and optionally installs it into the game's mods folder.

.EXAMPLE
    .\build.ps1                      # build only
    .\build.ps1 -Install             # build and copy to <game>\mods
    .\build.ps1 -GameDir "D:\...\Slay the Spire 2" -Install
#>
param(
    [string]$GameDir = $(if ($env:STS2_GAME_DIR) { $env:STS2_GAME_DIR } else { "D:\SteamLibrary\steamapps\common\Slay the Spire 2" }),
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Install
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path (Join-Path $GameDir "data_sts2_windows_x86_64\sts2.dll"))) {
    Write-Host "ERROR: sts2.dll not found under '$GameDir'." -ForegroundColor Red
    exit 1
}

$project = Join-Path $PSScriptRoot "STS2AgentMcp.csproj"
$outDir = Join-Path $PSScriptRoot "out"

dotnet build $project -c $Configuration -o $outDir -p:STS2GameDir="$GameDir" -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Install) {
    $modsDir = Join-Path $GameDir "mods"
    New-Item -ItemType Directory -Force $modsDir | Out-Null
    Copy-Item (Join-Path $outDir "STS2AgentMcp.dll") $modsDir -Force
    Copy-Item (Join-Path $PSScriptRoot "mod_manifest.json") (Join-Path $modsDir "STS2AgentMcp.json") -Force
    Write-Host "Installed to $modsDir (restart the game to load)." -ForegroundColor Green
}
