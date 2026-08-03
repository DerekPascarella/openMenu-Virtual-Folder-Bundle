# Normalizes .xaml and .axaml line endings to LF.
#
# dotnet format only reads C#, so .editorconfig's end_of_line rule never reaches XAML.
# Run -DryRun to list what would change without writing.

[CmdletBinding()]
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'src'

if (-not (Test-Path -LiteralPath $src)) {
    Write-Host "No src directory, nothing to do."
    exit 0
}

$skip = '\\(bin|obj|Aaru|DiscUtils|PuyoTools|SevenZip|tools|runtimes)\\'

# Filter with Where-Object rather than -Include. -Include is silently ignored
# alongside -LiteralPath, which once let a run reach binary files.
$targets = Get-ChildItem -LiteralPath $src -Recurse -File |
    Where-Object { $_.Extension -eq '.xaml' -or $_.Extension -eq '.axaml' } |
    Where-Object { $_.FullName -notmatch $skip }

foreach ($t in $targets) {
    if ($t.Extension -ne '.xaml' -and $t.Extension -ne '.axaml') {
        Write-Error "Refusing to run: '$($t.FullName)' is not XAML."
        exit 1
    }
}

$changed = 0
$skipped = 0

foreach ($t in $targets) {
    $bytes = [System.IO.File]::ReadAllBytes($t.FullName)

    # A NUL byte means this is not text. Every file damaged by the earlier run had one.
    $probe = [Math]::Min(8192, $bytes.Length)
    $binary = $false
    for ($i = 0; $i -lt $probe; $i++) {
        if ($bytes[$i] -eq 0) { $binary = $true; break }
    }
    if ($binary) {
        Write-Warning "Skipped, contains NUL bytes: $($t.FullName)"
        $skipped++
        continue
    }

    $out = New-Object 'System.Collections.Generic.List[byte]' ($bytes.Length)
    $removed = 0
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 13 -and ($i + 1) -lt $bytes.Length -and $bytes[$i + 1] -eq 10) {
            $removed++
            continue
        }
        $out.Add($bytes[$i])
    }

    if ($removed -eq 0) { continue }

    # The only bytes dropped must be the CR of a CRLF pair.
    if (($bytes.Length - $out.Count) -ne $removed) {
        Write-Error "Refusing to write '$($t.FullName)': unexpected byte count."
        exit 1
    }

    $rel = $t.FullName.Substring($PSScriptRoot.Length + 1)
    if ($DryRun) {
        Write-Host "  would fix: $rel ($removed CR bytes)"
    } else {
        [System.IO.File]::WriteAllBytes($t.FullName, $out.ToArray())
        Write-Host "  LF: $rel"
    }
    $changed++
}

$verb = if ($DryRun) { "would be rewritten" } else { "rewritten" }
Write-Host "XAML line endings: $changed of $($targets.Count) files $verb, $skipped skipped."
exit 0
