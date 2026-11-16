<#
.SYNOPSIS
    Records the source hashes and game version in knowledge-base documents (front matter `sources:` / `game_version:`).
    Run after writing or rewriting documents from a decompiled version.

.EXAMPLE
    .\kb-stamp.ps1 -Decompiled C:\WorkSpace\sts2-decompiled\v0.107.1 -Version v0.107.1
    .\kb-stamp.ps1 -Decompiled ... -Version ... -Files ..\kb\monsters\LAGAVULIN_MATRIARCH.md
#>
param(
    [Parameter(Mandatory = $true)][string]$Decompiled,
    [Parameter(Mandatory = $true)][string]$Version,
    [string[]]$Files
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "kb-common.ps1")
$kb = Join-Path (Split-Path $PSScriptRoot -Parent) "kb"
if (-not $Files) { $Files = Get-ChildItem $kb -Recurse -Filter *.md | Where-Object { $_.Name -ne "README.md" } | ForEach-Object FullName }

$problems = 0
foreach ($file in $Files) {
    $doc = Read-KbDoc $file
    if ($doc -eq $null) { Write-Warning "no front matter: $file"; $problems++; continue }

    $lines = @()
    $inSources = $false
    $wroteVersion = $false
    foreach ($line in ($doc.FrontMatter -split "\r?\n")) {
        if ($line -match '^game_version:') { continue }
        if ($line -match '^sources:') { $inSources = $true; $lines += $line; continue }
        if ($inSources -and $line -match '^\s*-\s+(?<path>\S+)') {
            $path = $Matches['path']
            $hash = Get-SourceHash $Decompiled $path
            if ($hash -eq $null) { Write-Warning "missing source $path in $file"; $problems++; $lines += "- $path"; continue }
            $lines += "- $path sha256:$hash"
            continue
        }
        $inSources = $false
        $lines += $line
        if ($line -match '^name:' -and -not $wroteVersion) { $lines += "game_version: $Version"; $wroteVersion = $true }
    }
    if (-not $wroteVersion) { $lines = @("game_version: $Version") + $lines }

    $newText = "---`n" + ($lines -join "`n") + "`n---`n" + $doc.Body
    [System.IO.File]::WriteAllText($file, $newText, $script:Utf8)
}
Write-Host "stamped $($Files.Count) document(s), $problems problem(s)"
