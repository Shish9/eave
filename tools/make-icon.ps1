# Draws Eave's mark (dark rounded shape, notch cut into the top, blue dot) and writes:
#   ClaudeIsland\eave.ico  - app/tray icon: the mark on a white rounded square, PNG images at Windows sizes
#   docs\logo.png          - the mark alone on transparent, for the README
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$dark = [System.Drawing.Color]::FromArgb(255, 47, 48, 51)
$blue = [System.Drawing.Color]::FromArgb(255, 0, 119, 255)

function Png($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single](2 * $r)
    $p.AddArc([single]$x, [single]$y, $d, $d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, $d, $d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# The mark, proportions taken from the logo: 596 wide, 326 tall, notch radius 75, dot radius 74 at 57% height.
function DrawMark($g, [single]$cx, [single]$cy, [single]$width, $cutColor) {
    $k = $width / 596.0
    $w = 596 * $k; $h = 326 * $k
    $x = $cx - $w / 2; $y = $cy - $h / 2
    $g.FillPath((New-Object System.Drawing.SolidBrush $dark), (RoundRect $x $y $w $h (158 * $k)))

    $nr = 75 * $k   # notch: a half circle cut down from the top edge
    if ($cutColor -eq $null) {
        $g.CompositingMode = 'SourceCopy'
        $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)), [single]($cx - $nr), [single]($y - $nr), [single](2*$nr), [single](2*$nr))
        $g.CompositingMode = 'SourceOver'
    } else {
        $g.FillEllipse((New-Object System.Drawing.SolidBrush $cutColor), [single]($cx - $nr), [single]($y - $nr), [single](2*$nr), [single](2*$nr))
    }

    $dr = 74 * $k
    $dy = $y + 185 * $k
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), [single]($cx - $dr), [single]($dy - $dr), [single](2*$dr), [single](2*$dr))
}

function DrawIconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $white = [System.Drawing.Color]::White
    $g.FillPath((New-Object System.Drawing.SolidBrush $white), (RoundRect 0 0 ($s - 1) ($s - 1) ($s * 0.22)))
    DrawMark $g ($s / 2) ($s / 2) ($s * 0.82) $white
    $g.Dispose()
    return $bmp
}

# 256 px is stored as PNG; smaller sizes as a classic 32-bit DIB (BITMAPINFOHEADER + pixels + AND mask),
# which every Windows icon loader understands.
function DrawIcon([int]$s) {
    $bmp = DrawIconBitmap $s
    if ($s -ge 256) { return Png $bmp }
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$s); $w.Write([int32]($s * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]0); $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $s - 1; $y -ge 0; $y--) {          # bottom-up BGRA
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)   # AND mask: all zero (alpha channel does the work)
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    $bmp.Dispose()
    return ,$ms.ToArray()
}

function DrawLogo([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, ([int]($s * 0.6)), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    DrawMark $g ($s / 2) ($s * 0.3) ($s * 0.9) $null
    $g.Dispose()
    return Png $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) { ,(DrawIcon $s) }

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                      # ICONDIRENTRY
    $s = $sizes[$i]; $len = $images[$i].Length
    $b = [byte]($(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write($b); $bw.Write($b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()

$root = Join-Path $PSScriptRoot '..'
New-Item -ItemType Directory -Force (Join-Path $root 'docs') | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $root 'ClaudeIsland\eave.ico'), $out.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $root 'docs\icon-256.png'), $images[-1])
[System.IO.File]::WriteAllBytes((Join-Path $root 'docs\logo.png'), (DrawLogo 512))
"wrote eave.ico, docs\icon-256.png, docs\logo.png"
