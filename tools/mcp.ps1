<#
.SYNOPSIS
    Calls the in-game MCP endpoint directly (for development/testing without an MCP client).

.EXAMPLE
    .\mcp.ps1 init
    .\mcp.ps1 list
    .\mcp.ps1 state
    .\mcp.ps1 state full
    .\mcp.ps1 act "play c3 @e1" "end"
    .\mcp.ps1 info BASH CARD.STRIKE_IRONCLAD
#>
param(
    [Parameter(Mandatory = $true, Position = 0)][ValidateSet("init", "list", "state", "act", "info")][string]$Command,
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Rest,
    [string]$GameDir = $(if ($env:STS2_GAME_DIR) { $env:STS2_GAME_DIR } else { "D:\SteamLibrary\steamapps\common\Slay the Spire 2" })
)

$ErrorActionPreference = "Stop"
$conf = Get-Content (Join-Path $GameDir "mods\STS2AgentMcp.conf") -Raw | ConvertFrom-Json
$uri = "http://127.0.0.1:$($conf.port)/mcp"

switch ($Command) {
    "init"  { $method = "initialize"; $params = @{ protocolVersion = "2025-06-18"; capabilities = @{}; clientInfo = @{ name = "mcp.ps1"; version = "1" } } }
    "list"  { $method = "tools/list"; $params = @{} }
    "state" { $method = "tools/call"; $params = @{ name = "state"; arguments = @{ detail = $(if ($Rest -and $Rest[0] -eq "full") { "full" } else { "brief" }) } } }
    "act"   { $method = "tools/call"; $params = @{ name = "act"; arguments = @{ actions = @($Rest) } } }
    "info"  { $method = "tools/call"; $params = @{ name = "info"; arguments = @{ ids = @($Rest) } } }
}

$body = @{ jsonrpc = "2.0"; id = 1; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress
$response = Invoke-RestMethod -Uri $uri -Method Post -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $($conf.token)" } -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 120

if ($response.error) { Write-Host "ERROR $($response.error.code): $($response.error.message)" -ForegroundColor Red; exit 1 }
if ($method -eq "tools/call") {
    if ($response.result.isError) { Write-Host "(isError)" -ForegroundColor Yellow }
    $response.result.content | ForEach-Object { $_.text }
} else {
    $response.result | ConvertTo-Json -Depth 10
}
