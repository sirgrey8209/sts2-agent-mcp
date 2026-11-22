<#
.SYNOPSIS
    Development loop: build, quit the game, install the mods, relaunch through Steam, wait until the mods are ready.

.EXAMPLE
    .\redeploy.ps1                 # both mods
    .\redeploy.ps1 -Mods mcp       # external MCP only (the chat mod is taken out of the mods folder)
    .\redeploy.ps1 -Mods chat      # in-game chat only
#>
param(
    [string]$GameDir = $(if ($env:STS2_GAME_DIR) { $env:STS2_GAME_DIR } else { "D:\SteamLibrary\steamapps\common\Slay the Spire 2" }),
    [ValidateSet("mcp", "chat")]
    [string[]]$Mods = @("mcp", "chat"),
    [int]$TimeoutSec = 180
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$log = Join-Path $env:APPDATA "SlayTheSpire2\logs\godot.log"

# Build first (the game keeps running if the build fails).
& (Join-Path $root "build.ps1") -GameDir $GameDir -Mods $Mods
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue) {
    Stop-Process -Name SlayTheSpire2
    while (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
    Start-Sleep -Seconds 2
}

& (Join-Path $root "build.ps1") -GameDir $GameDir -Mods $Mods -Install
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$ready = @()
if ($Mods -contains "mcp") { $ready += "\[STS2 Agent MCP\] ready" }
if ($Mods -contains "chat") { $ready += "\[STS2 Agent Chat\] ready" }

$launchedAt = Get-Date
Start-Process "steam://rungameid/2868840"
$deadline = $launchedAt.AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    if (-not (Test-Path $log) -or (Get-Item $log).LastWriteTime -le $launchedAt) { continue }
    $missing = $ready | Where-Object { -not (Select-String -Path $log -Pattern $_ -Quiet) }
    if (-not $missing -and (Select-String -Path $log -Pattern "Time to main menu" -Quiet)) { break }
}
Select-String -Path $log -Pattern "STS2 Agent (MCP|Chat)\]" | ForEach-Object { $_.Line }
