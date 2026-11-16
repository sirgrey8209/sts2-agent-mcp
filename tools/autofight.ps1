<#
.SYNOPSIS
    Development helper: finishes the current combat with a naive rule (spend energy on attacks at the first enemy,
    then skills, end turn; answer card choices with the first option). Used to reach other screens quickly in tests.
    Not a playing agent. (ASCII only: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.)
#>
param([int]$MaxTurns = 30)

$mcp = Join-Path $PSScriptRoot "mcp.ps1"
$unplayable = [string][char]0x2717
for ($turn = 0; $turn -lt $MaxTurns; $turn++) {
    $state = (& $mcp state) -join "`n"
    if ($state -match "== choice ==") { & $mcp act "choose 0" | Out-Null; continue }
    if ($state -notmatch "== combat ==") { break }
    if ($state -notmatch "your turn") { & $mcp act "wait" | Out-Null; continue }

    $energy = if ($state -match "energy (\d+)/") { [int]$Matches[1] } else { 3 }
    $enemy = if ($state -match "(?m)^  (e\d+) ") { $Matches[1] } else { $null }
    $cards = foreach ($m in [regex]::Matches($state, "(?m)^  (c\d+) .*? \[(\d+)E[^\]]*\] (Attack|Skill|Power)(.*)$")) {
        if ($m.Groups[4].Value.Contains($unplayable)) { continue }
        [pscustomobject]@{
            Id = $m.Groups[1].Value
            Cost = [int]$m.Groups[2].Value
            Type = $m.Groups[3].Value
            Targeted = $m.Groups[4].Value.Contains("@enemy")
        }
    }

    $actions = @()
    foreach ($type in @("Power", "Attack", "Skill")) {
        foreach ($card in ($cards | Where-Object { $_.Type -eq $type } | Sort-Object Cost -Descending)) {
            if ($card.Cost -le $energy) {
                if ($card.Targeted -and $enemy) { $actions += "play $($card.Id) @$enemy" } else { $actions += "play $($card.Id)" }
                $energy -= $card.Cost
            }
        }
    }
    $actions += "end"
    $result = (& $mcp act @actions) -join "`n"
    if ($result -match "victory|== rewards ==|== game_over ==") { break }
}
(& $mcp state) -join "`n"
