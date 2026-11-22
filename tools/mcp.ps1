<#
.SYNOPSIS
    Calls a mod's MCP endpoint directly (for development/testing without an MCP client): STS2AgentMcp's /mcp by
    default, STS2AgentChat's /mcp/dev with -Dev or /mcp/chat with -Chat.

.EXAMPLE
    .\mcp.ps1 init
    .\mcp.ps1 list
    .\mcp.ps1 state
    .\mcp.ps1 state full
    .\mcp.ps1 act "play c3 @e1" "end"
    .\mcp.ps1 info BASH CARD.STRIKE_IRONCLAD
    .\mcp.ps1 call screenshot '{}'
    .\mcp.ps1 call render_cards '{"source":"hand"}'
    .\mcp.ps1 -Dev call dev_layout '{"action":"auth"}'
    .\mcp.ps1 -Chat list
#>
param(
    [Parameter(Mandatory = $true, Position = 0)][ValidateSet("init", "list", "state", "act", "info", "call")][string]$Command,
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Rest,
    [string]$GameDir = $(if ($env:STS2_GAME_DIR) { $env:STS2_GAME_DIR } else { "D:\SteamLibrary\steamapps\common\Slay the Spire 2" }),
    [switch]$Dev,
    [switch]$Chat
)

$ErrorActionPreference = "Stop"
$confName = if ($Dev -or $Chat) { "STS2AgentChat.conf" } else { "STS2AgentMcp.conf" }
$path = if ($Dev) { "/mcp/dev" } elseif ($Chat) { "/mcp/chat" } else { "/mcp" }
$conf = Get-Content (Join-Path $GameDir "mods\$confName") -Raw | ConvertFrom-Json
$uri = "http://127.0.0.1:$($conf.port)$path"

switch ($Command) {
    "init"  { $method = "initialize"; $params = @{ protocolVersion = "2025-06-18"; capabilities = @{}; clientInfo = @{ name = "mcp.ps1"; version = "1" } } }
    "list"  { $method = "tools/list"; $params = @{} }
    "state" { $method = "tools/call"; $params = @{ name = "state"; arguments = @{ detail = $(if ($Rest -and $Rest[0] -eq "full") { "full" } else { "brief" }) } } }
    "act"   { $method = "tools/call"; $params = @{ name = "act"; arguments = @{ actions = @($Rest) } } }
    "info"  { $method = "tools/call"; $params = @{ name = "info"; arguments = @{ ids = @($Rest) } } }
    "call"  { $method = "tools/call"; $params = @{ name = $Rest[0]; arguments = $(if ($Rest.Count -gt 1) { $Rest[1] | ConvertFrom-Json } else { @{} }) } }
}

$body = @{ jsonrpc = "2.0"; id = 1; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress
$response = Invoke-RestMethod -Uri $uri -Method Post -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $($conf.token)" } -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 120

if ($response.error) { Write-Host "ERROR $($response.error.code): $($response.error.message)" -ForegroundColor Red; exit 1 }
if ($method -eq "tools/call") {
    if ($response.result.isError) { Write-Host "(isError)" -ForegroundColor Yellow }
    $response.result.content | ForEach-Object { if ($_.type -eq "image") { "[image $($_.mimeType), $($_.data.Length) base64 chars]" } else { $_.text } }
} else {
    $response.result | ConvertTo-Json -Depth 10
}
