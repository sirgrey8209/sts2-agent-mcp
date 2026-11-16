<#
.SYNOPSIS
    Development loop: build, quit the game, install the mod, relaunch through Steam, wait until the mod is ready.
#>
param(
    [string]$GameDir = $(if ($env:STS2_GAME_DIR) { $env:STS2_GAME_DIR } else { "D:\SteamLibrary\steamapps\common\Slay the Spire 2" }),
    [int]$TimeoutSec = 180
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$log = Join-Path $env:APPDATA "SlayTheSpire2\logs\godot.log"

dotnet build (Join-Path $root "STS2AgentMcp.csproj") -c Release -o (Join-Path $root "out") -nologo -v q -p:STS2GameDir="$GameDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue) {
    Stop-Process -Name SlayTheSpire2
    while (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
    Start-Sleep -Seconds 2
}

Copy-Item (Join-Path $root "out\STS2AgentMcp.dll") (Join-Path $GameDir "mods") -Force
Copy-Item (Join-Path $root "mod_manifest.json") (Join-Path $GameDir "mods\STS2AgentMcp.json") -Force

$launchedAt = Get-Date
Start-Process "steam://rungameid/2868840"
$deadline = $launchedAt.AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    if ((Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $launchedAt -and
        (Select-String -Path $log -Pattern "fast mode set to Instant|\[STS2 Agent MCP\] ready" -Quiet)) {
        if (Select-String -Path $log -Pattern "fast mode set to Instant" -Quiet) { break }
    }
}
Select-String -Path $log -Pattern "STS2 Agent MCP" | ForEach-Object { $_.Line }
