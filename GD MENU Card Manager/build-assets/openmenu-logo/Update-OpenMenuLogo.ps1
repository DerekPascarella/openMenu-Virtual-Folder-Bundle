[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$menu = Join-Path $root 'src\GDMENUCardManager.Core\tools\openMenu'
$ipPath = Join-Path $menu 'IP.BIN'
$ip = [IO.File]::ReadAllBytes($ipPath)
if ($ip.Length -ne 32768 -or [Text.Encoding]::ASCII.GetString($ip, 0, 16) -ne 'SEGA SEGAKATANA ') {
    throw 'Expected a 32768-byte Dreamcast IP.BIN.'
}
if ([string]::IsNullOrWhiteSpace($Version)) { throw 'The version is empty.' }

function ConvertTo-Mr([Drawing.Bitmap]$Bitmap) {
    $palette = [Collections.Generic.List[Drawing.Color]]::new()
    $lookup = @{}
    $indices = New-Object byte[] ($Bitmap.Width * $Bitmap.Height)
    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            $color = $Bitmap.GetPixel($x, $y)
            if ($color.A -ne 255) { throw 'The logo template must be opaque.' }
            $key = $color.ToArgb()
            if (-not $lookup.ContainsKey($key)) {
                if ($palette.Count -eq 128) { throw 'The logo exceeds 128 colors.' }
                $lookup[$key] = $palette.Count
                $palette.Add($color)
            }
            $indices[$y * $Bitmap.Width + $x] = $lookup[$key]
        }
    }

    $encoded = [Collections.Generic.List[byte]]::new()
    for ($i = 0; $i -lt $indices.Length;) {
        $count = 1
        while ($count -lt 383 -and $i + $count -lt $indices.Length -and
               $indices[$i + $count] -eq $indices[$i]) { $count++ }
        if ($count -gt 255) {
            $encoded.Add(0x82)
            $encoded.Add(0x80 -bor ($count - 256))
        } elseif ($count -gt 127) {
            $encoded.Add(0x81)
            $encoded.Add($count)
        } elseif ($count -gt 1) {
            $encoded.Add(0x80 -bor $count)
        }
        $encoded.Add($indices[$i])
        $i += $count
    }

    $offset = 30 + 4 * $palette.Count
    $size = $offset + $encoded.Count
    if ($size -gt 8192) { throw 'The logo exceeds the 8192-byte MR limit.' }
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([byte[]](0x4d, 0x52))
        foreach ($value in @($size, 0, $offset, $Bitmap.Width, $Bitmap.Height, 0, $palette.Count)) {
            $writer.Write([uint32]$value)
        }
        foreach ($color in $palette) {
            $writer.Write([byte[]]($color.B, $color.G, $color.R, 0))
        }
        $writer.Write($encoded.ToArray())
        return ,$stream.ToArray()
    } finally {
        $writer.Dispose()
    }
}

$font = [IO.File]::ReadAllBytes((Join-Path $menu 'menu_data\font\GDMNUFNT.pvr'))
if ($font.Length -ne 32800 -or [Text.Encoding]::ASCII.GetString($font, 16, 4) -ne 'PVRT' -or
    $font[24] -ne 0 -or $font[25] -ne 1 -or
    [BitConverter]::ToUInt16($font, 28) -ne 128 -or
    [BitConverter]::ToUInt16($font, 30) -ne 128) {
    throw 'Expected a 128 x 128, twiddled ARGB1555 GDMNUFNT.pvr.'
}

$spread = New-Object int[] 128
for ($i = 0; $i -lt 128; $i++) {
    for ($bit = 0; $bit -lt 7; $bit++) {
        $spread[$i] = $spread[$i] -bor ((($i -shr $bit) -band 1) -shl (2 * $bit))
    }
}
$points = [Collections.Generic.List[Drawing.Point]]::new()
for ($i = 0; $i -lt $Version.Length; $i++) {
    $code = [int][char]$Version[$i]
    if ($code -lt 32 -or $code -gt 126) { throw 'The version contains an unsupported character.' }
    $glyph = $code - 32
    $before = $points.Count
    for ($y = 0; $y -lt 16; $y++) {
        for ($x = 0; $x -lt 8; $x++) {
            $fx = ($glyph % 16) * 8 + $x
            $fy = [int][Math]::Floor($glyph / 16) * 16 + $y
            $index = $spread[$fy] -bor ($spread[$fx] -shl 1)
            $pixel = [BitConverter]::ToUInt16($font, 32 + 2 * $index)
            if (($pixel -band 0x8000) -ne 0) {
                $points.Add([Drawing.Point]::new($i * 8 + $x, $y))
            }
        }
    }
    if ($code -ne 32 -and $points.Count -eq $before) { throw 'A version character is missing from the font.' }
}

$minX = ($points.X | Measure-Object -Minimum).Minimum
$maxX = ($points.X | Measure-Object -Maximum).Maximum
$width = $maxX - $minX + 1
if ($width -gt 126) { throw 'The version is too wide for the logo.' }

# The template reserves x=176..301 and y=58..73 for the version glyphs.
$left = 176 + [int][Math]::Floor((126 - $width) / 2)
$bitmap = [Drawing.Bitmap]::new((Join-Path $PSScriptRoot 'base.png'))
try {
    if ($bitmap.Width -ne 320 -or $bitmap.Height -ne 90) { throw 'Expected a 320 x 90 logo template.' }
    $textColor = [Drawing.Color]::FromArgb(192, 192, 192)
    foreach ($point in $points) {
        $bitmap.SetPixel($left + $point.X - $minX, 58 + $point.Y, $textColor)
    }
    $mr = ConvertTo-Mr $bitmap
} finally {
    $bitmap.Dispose()
}

$changed = $false
for ($i = 0; $i -lt $mr.Length; $i++) {
    if ($ip[0x3820 + $i] -ne $mr[$i]) { $changed = $true; break }
}
if ($changed) {
    [Array]::Copy($mr, 0, $ip, 0x3820, $mr.Length)
    [IO.File]::WriteAllBytes($ipPath, $ip)
    Write-Host "Updated openMenu boot logo: $Version ($($mr.Length) bytes)."
} else {
    Write-Host "openMenu boot logo is current: $Version."
}
