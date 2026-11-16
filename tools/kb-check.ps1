<#
.SYNOPSIS
    Compares knowledge-base documents against a decompiled game version: documents whose sources changed or vanished,
    and monsters/events without a document.

.EXAMPLE
    .\kb-check.ps1 -Decompiled C:\WorkSpace\sts2-decompiled\v0.108.0
#>
param([Parameter(Mandatory = $true)][string]$Decompiled)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "kb-common.ps1")
$kb = Join-Path (Split-Path $PSScriptRoot -Parent) "kb"

$stale = @()
foreach ($file in (Get-ChildItem $kb -Recurse -Filter *.md | Where-Object { $_.Name -ne "README.md" })) {
    $doc = Read-KbDoc $file.FullName
    if ($doc -eq $null) { $stale += [pscustomobject]@{ Doc = $file.Name; Reason = "no front matter" }; continue }
    foreach ($src in $doc.Sources) {
        $now = Get-SourceHash $Decompiled $src.Path
        if ($now -eq $null) { $stale += [pscustomobject]@{ Doc = $file.Name; Reason = "source removed: $($src.Path)" } }
        elseif (-not $src.Hash) { $stale += [pscustomobject]@{ Doc = $file.Name; Reason = "not stamped: $($src.Path)" } }
        elseif ($now -ne $src.Hash.ToLowerInvariant()) { $stale += [pscustomobject]@{ Doc = $file.Name; Reason = "changed: $($src.Path)" } }
    }
}

$missing = @()
foreach ($kind in @(@{ Folder = "MegaCrit.Sts2.Core.Models.Monsters"; Kb = "monsters" }, @{ Folder = "MegaCrit.Sts2.Core.Models.Events"; Kb = "events" })) {
    $dir = Join-Path $Decompiled $kind.Folder
    if (-not (Test-Path $dir)) { continue }
    foreach ($cs in Get-ChildItem $dir -Filter *.cs) {
        $entry = Get-ModelEntry $cs.BaseName
        if (-not (Test-Path (Join-Path $kb "$($kind.Kb)\$entry.md"))) {
            $missing += [pscustomobject]@{ Kind = $kind.Kb; Entry = $entry; Source = "$($kind.Folder)/$($cs.Name)" }
        }
    }
}

Write-Host "== stale documents: $($stale.Count)"
$stale | Sort-Object Doc | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
Write-Host "== monsters/events without a document: $($missing.Count)"
$missing | Sort-Object Kind, Entry | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
