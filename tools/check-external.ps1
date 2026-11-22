<#
.SYNOPSIS
    Regression check for external MCP clients (docs/chat-roadmap.md): STS2AgentMcp's /mcp endpoint must keep its tool
    list and answer read-only calls; when the chat mod (STS2AgentChat) is installed, its /mcp/chat endpoint must list
    the chat's tools. Nothing here changes the game (no act).

.EXAMPLE
    .\check-external.ps1
#>
param(
    [string]$GameDir = $(if ($env:STS2_GAME_DIR) { $env:STS2_GAME_DIR } else { "D:\SteamLibrary\steamapps\common\Slay the Spire 2" })
)

$ErrorActionPreference = "Stop"
$conf = Get-Content (Join-Path $GameDir "mods\STS2AgentMcp.conf") -Raw | ConvertFrom-Json
$chatConfFile = Join-Path $GameDir "mods\STS2AgentChat.conf"
$chatConf = if ((Test-Path $chatConfFile) -and (Test-Path (Join-Path $GameDir "mods\STS2AgentChat.dll"))) { Get-Content $chatConfFile -Raw | ConvertFrom-Json } else { $null }
$failed = 0

function Invoke-Mcp([string]$path, [string]$method, $params, $target = $conf) {
    $body = @{ jsonrpc = "2.0"; id = 1; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress
    Invoke-RestMethod -Uri ("http://127.0.0.1:$($target.port)" + $path) -Method Post -ContentType "application/json" `
        -Headers @{ Authorization = "Bearer $($target.token)" } -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 120
}

function Report([string]$name, [bool]$ok, [string]$detail = "") {
    if ($ok) { Write-Host "PASS $name" -ForegroundColor Green }
    else { Write-Host "FAIL $name $detail" -ForegroundColor Red; $script:failed++ }
}

$external = @("state", "act", "info", "search", "screenshot", "render_cards", "render_map", "export_loc")
$optional = @("dev_console")
$chat = @("state", "act", "info", "search", "show_cards", "show_relics", "open_view", "map_route", "notes", "play_level", "run_brief", "AskUserQuestion", "wait_for_player")

$list = (Invoke-Mcp "/mcp" "tools/list" @{}).result.tools.name
$missing = $external | Where-Object { $list -notcontains $_ }
$extra = $list | Where-Object { $external -notcontains $_ -and $optional -notcontains $_ }
Report "/mcp tool list unchanged" (($missing.Count -eq 0) -and ($extra.Count -eq 0)) "missing: $($missing -join ',') extra: $($extra -join ',')"

if ($chatConf) {
    $chatList = (Invoke-Mcp "/mcp/chat" "tools/list" @{} $chatConf).result.tools.name
    $chatMissing = $chat | Where-Object { $chatList -notcontains $_ }
    Report "/mcp/chat has every chat tool" ($chatMissing.Count -eq 0) "missing: $($chatMissing -join ',')"
} else {
    Write-Host "SKIP /mcp/chat (STS2AgentChat not installed)"
}

$calls = @(
    @{ name = "state"; arguments = @{ detail = "brief" } },
    @{ name = "state"; arguments = @{ detail = "full" } },
    @{ name = "info"; arguments = @{ ids = @("BASH") } },
    @{ name = "search"; arguments = @{ query = "strike" } },
    @{ name = "render_cards"; arguments = @{ cards = @("BASH") } }
)
foreach ($call in $calls) {
    $result = (Invoke-Mcp "/mcp" "tools/call" $call).result
    $text = ($result.content | Where-Object { $_.type -eq "text" } | Select-Object -First 1).text
    $label = "$($call.name) $(($call.arguments | ConvertTo-Json -Compress))"
    Report $label ((-not $result.isError) -and $text) $text
}

$init = (Invoke-Mcp "/mcp" "initialize" @{ protocolVersion = "2025-06-18"; capabilities = @{}; clientInfo = @{ name = "check"; version = "1" } }).result
Report "initialize" ($init.serverInfo.name -eq "sts2")

if ($failed -gt 0) { Write-Host "$failed check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host "all checks passed" -ForegroundColor Green
