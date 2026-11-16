# Shared helpers for kb-stamp.ps1 / kb-check.ps1 (dot-source). ASCII only: Windows PowerShell 5.1 reads
# BOM-less scripts as ANSI. Document files are read and written as UTF-8 through .NET APIs.

$script:Utf8 = New-Object System.Text.UTF8Encoding $false

function Get-ModelEntry([string]$className) {
    # Same rule as the game's StringHelper.Slugify (model id entry): LagavulinMatriarch -> LAGAVULIN_MATRIARCH
    $s = [regex]::Replace($className.Trim(), '([A-Za-z0-9]|\G(?!^))([A-Z])', '$1_$2').ToUpperInvariant()
    $s = [regex]::Replace($s, '\s+', '_')
    return [regex]::Replace($s, '[^A-Z0-9_]', '')
}

function Read-KbDoc([string]$path) {
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $m = [regex]::Match($text, '^---\r?\n(?<fm>.*?)\r?\n---\r?\n', 'Singleline')
    if (-not $m.Success) { return $null }
    $sources = @()
    $version = $null
    foreach ($line in ($m.Groups['fm'].Value -split "\r?\n")) {
        if ($line -match '^\s*-\s+(?<path>\S+)(\s+sha256:(?<hash>[0-9A-Fa-f]+))?\s*$') {
            $sources += [pscustomobject]@{ Path = $Matches['path']; Hash = $Matches['hash'] }
        }
        elseif ($line -match '^game_version:\s*(?<v>\S+)') { $version = $Matches['v'] }
    }
    return [pscustomobject]@{
        Path = $path; Text = $text; FrontMatter = $m.Groups['fm'].Value; Body = $text.Substring($m.Length)
        Sources = $sources; Version = $version
    }
}

function Get-SourceHash([string]$decompiled, [string]$relative) {
    $full = Join-Path $decompiled ($relative -replace '/', '\')
    if (-not (Test-Path $full)) { return $null }
    return (Get-FileHash -Algorithm SHA256 $full).Hash.ToLowerInvariant()
}
