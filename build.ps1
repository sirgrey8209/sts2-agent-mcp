<#
.SYNOPSIS
    Builds the two mods (STS2AgentMcp: external MCP, STS2AgentChat: in-game chat) and optionally installs them into
    the game's mods folder.

.EXAMPLE
    .\build.ps1                          # build both
    .\build.ps1 -Install                 # build both and copy to <game>\mods
    .\build.ps1 -Mods mcp -Install       # external MCP only (the chat mod is taken out of the mods folder)
    .\build.ps1 -Mods chat -Install      # in-game chat only
    .\build.ps1 -GameDir "D:\...\Slay the Spire 2" -Install
#>
param(
    [string]$GameDir = $(if ($env:STS2_GAME_DIR) { $env:STS2_GAME_DIR } else { "D:\SteamLibrary\steamapps\common\Slay the Spire 2" }),
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("mcp", "chat")]
    [string[]]$Mods = @("mcp", "chat"),
    [switch]$Install
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path (Join-Path $GameDir "data_sts2_windows_x86_64\sts2.dll"))) {
    Write-Host "ERROR: sts2.dll not found under '$GameDir'." -ForegroundColor Red
    exit 1
}

# id, project folder, project file
$all = @(
    @{ Key = "mcp"; Id = "STS2AgentMcp"; Dir = "External" },
    @{ Key = "chat"; Id = "STS2AgentChat"; Dir = "Chat" }
)
$outDir = Join-Path $PSScriptRoot "out"

foreach ($mod in $all | Where-Object { $Mods -contains $_.Key }) {
    $project = Join-Path $PSScriptRoot "src\$($mod.Dir)\$($mod.Id).csproj"
    dotnet build $project -c $Configuration -o $outDir -p:STS2GameDir="$GameDir" -nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($Install) {
    $modsDir = Join-Path $GameDir "mods"
    New-Item -ItemType Directory -Force $modsDir | Out-Null
    $kb = Join-Path $PSScriptRoot "kb"
    foreach ($mod in $all) {
        $kbDir = Join-Path $modsDir "$($mod.Id)-kb"
        if ($Mods -contains $mod.Key) {
            Copy-Item (Join-Path $outDir "$($mod.Id).dll") $modsDir -Force
            Copy-Item (Join-Path $PSScriptRoot "src\$($mod.Dir)\mod_manifest.json") (Join-Path $modsDir "$($mod.Id).json") -Force
            # Knowledge base documents served by `info` (mirror: removed docs are removed too).
            if (Test-Path $kb) { robocopy $kb $kbDir /MIR /NFL /NDL /NJH /NJS | Out-Null }
            if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }  # 0-7 mean success
            $global:LASTEXITCODE = 0
        } else {
            # Left out: take it out of the mods folder, keeping its .conf (token and settings).
            Remove-Item (Join-Path $modsDir "$($mod.Id).dll"), (Join-Path $modsDir "$($mod.Id).json") -ErrorAction SilentlyContinue
            if (Test-Path $kbDir) { Remove-Item $kbDir -Recurse -Force }
        }
    }
    Write-Host "Installed $($Mods -join ', ') to $modsDir (restart the game to load)." -ForegroundColor Green
}
