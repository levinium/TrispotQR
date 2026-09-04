<#
.SYNOPSIS
    Generates src\TrispotQR.App\Resources\app.ico.

.DESCRIPTION
    The mark is the name: three spots, arranged as the three finder patterns of a QR code
    (top left, top right, bottom left), on a rounded navy tile. Nothing else. The fourth
    corner is deliberately empty, which is what a real QR code looks like.

    WHY THERE IS NOTHING ELSE IN IT. Earlier versions of this icon carried an anvil, a
    frame and a scatter of data modules. Every one of them turned to mush by 32 pixels,
    because the elements had to shrink to fit each other in. With the three spots alone
    each one is large enough to survive all the way down to 16 pixels, which is the size
    Explorer's Details view actually draws.

    ON TRANSPARENCY. This icon has transparent corners, which an earlier white-tiled
    version could not afford. That version was invisible against Explorer's white
    background, because a white tile bounded only by a hairline reads as no background at
    all. A dark navy tile has no such problem: it is unmistakable on white, on a dark
    taskbar, and on an accent colour alike. The rounded corners are safe here precisely
    because the tile is dark.

    GDI+ GOTCHA. With SmoothingMode.AntiAlias, FillRectangle offsets by half a pixel, so
    axis-aligned fills come out as two rows of half strength instead of one solid row.
    Nothing in this design relies on a hairline, so smoothing stays on throughout for the
    curves, but keep that in mind before adding any thin straight element.

    Run once, then commit the .ico. There is no need to re-run it unless the icon changes.

    IF EXPLORER STILL SHOWS THE OLD ICON, IT IS ALMOST CERTAINLY NOT THIS FILE.
    explorer.exe builds its small-icon image list in memory and does not re-read it for a
    path it has already seen, so a long-running Explorer keeps drawing a stale icon and F5
    just redraws the same stale copy. Every API check will report the new icon correctly,
    because each runs in a fresh process. The decisive test is to publish to a path Windows
    has never seen; if the icon is right there, only the running Explorer is stale, and
    restarting explorer.exe or signing out clears it.
#>

[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\TrispotQR.App\Resources\app.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)

# Diagonal gradient: lit at the top left, deepening to the bottom right, the way light
# conventionally falls. A vertical ramp was tried first and looks noticeably flatter.
$blueTopLeft     = [System.Drawing.Color]::FromArgb(255, 0x35, 0x66, 0xD8)
$navyBottomRight = [System.Drawing.Color]::FromArgb(255, 0x1B, 0x2E, 0x52)
$paper           = [System.Drawing.Color]::FromArgb(255, 250, 251, 253)

# A faint light rim just inside the tile edge. This is not decoration: the foot of the
# gradient is dark enough to merge into a dark taskbar, and the rim is what keeps the
# bottom right corner defined against #202020 and black.
$rimColor = [System.Drawing.Color]::FromArgb(70, 255, 255, 255)

function New-RoundedRect {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)

    $r = [Math]::Min([Math]::Max($R, 0), [Math]::Min($W, $H) / 2.0)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath

    if ($r -le 0.01) {
        $p.AddRectangle((New-Object System.Drawing.RectangleF $X, $Y, $W, $H))
        return $p
    }

    $d = $r * 2
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc(($X + $W - $d), $Y, $d, $d, 270, 90)
    $p.AddArc(($X + $W - $d), ($Y + $H - $d), $d, $d, 0, 90)
    $p.AddArc($X, ($Y + $H - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

<#
    One spot. A real QR finder pattern is a ring with a solid core, and that is drawn at
    every size big enough to hold it. Below 28 pixels the ring, its gap and the core each
    fall under a pixel and merge into a grey smudge, so the spot becomes a single solid
    square instead: less literal, but it still reads as a spot, which a smudge does not.
#>
function Add-Spot {
    param($G, [single]$Cx, [single]$Cy, [single]$S, $Brush, [bool]$Simplify)

    $radius = $S * 0.30

    if ($Simplify) {
        $solid = New-RoundedRect -X ($Cx - $S / 2) -Y ($Cy - $S / 2) -W $S -H $S -R $radius
        $G.FillPath($Brush, $solid)
        $solid.Dispose()
        return
    }

    $stroke = $S * 0.23
    $outer = New-RoundedRect -X ($Cx - $S / 2) -Y ($Cy - $S / 2) -W $S -H $S -R $radius
    $inner = New-RoundedRect -X ($Cx - $S / 2 + $stroke) -Y ($Cy - $S / 2 + $stroke) `
        -W ($S - 2 * $stroke) -H ($S - 2 * $stroke) -R (($S - 2 * $stroke) * 0.30)

    $ring = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ring.AddPath($outer, $false)
    $ring.AddPath($inner, $false)
    $ring.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
    $G.FillPath($Brush, $ring)
    $ring.Dispose(); $outer.Dispose(); $inner.Dispose()

    $core = $S * 0.32
    $cp = New-RoundedRect -X ($Cx - $core / 2) -Y ($Cy - $core / 2) -W $core -H $core -R ($core * 0.30)
    $G.FillPath($Brush, $cp)
    $cp.Dispose()
}

function New-IconPng {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # The tile is inset by half the rim width so the whole stroke lands inside the bitmap
    # rather than being clipped at the edge.
    $rimWidth = [Math]::Max(1.0, $Size / 32.0)
    $inset = $rimWidth / 2.0
    $tile = New-RoundedRect -X $inset -Y $inset -W ($Size - 2 * $inset) -H ($Size - 2 * $inset) `
        -R (($Size * 0.225) - $inset)

    # Endpoints pushed a pixel beyond the corners: a GDI+ linear gradient that starts
    # exactly on the shape edge can band or wrap on that first pixel.
    $ground = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF (-1), (-1)),
        (New-Object System.Drawing.PointF ([single]$Size + 1), ([single]$Size + 1)),
        $blueTopLeft, $navyBottomRight)
    $ground.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
    $g.FillPath($ground, $tile)
    $ground.Dispose()

    $rimPen = New-Object System.Drawing.Pen $rimColor, $rimWidth
    $g.DrawPath($rimPen, $tile)
    $rimPen.Dispose()
    $tile.Dispose()

    # Tighter margins at the smallest sizes, where every pixel spent on padding is a pixel
    # the spots do not get.
    $padFrac = $(if ($Size -lt 32) { 0.130 } else { 0.155 })
    $pad = $Size * $padFrac
    $box = $Size - (2 * $pad)
    $spot = $box * 0.42
    $half = $spot / 2

    $brush = New-Object System.Drawing.SolidBrush $paper
    $simplify = $Size -lt 28

    # Top left, top right, bottom left. The fourth corner stays empty, as on a real code.
    Add-Spot $g ($pad + $half) ($pad + $half) $spot $brush $simplify
    Add-Spot $g ($pad + $box - $half) ($pad + $half) $spot $brush $simplify
    Add-Spot $g ($pad + $half) ($pad + $box - $half) $spot $brush $simplify

    $brush.Dispose()
    $g.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $stream.ToArray()
}

# Collected through a typed list, because PowerShell unrolls a byte[] when it travels
# through the output stream and the entries would arrive as loose bytes.
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) { $images.Add((New-IconPng -Size $size)) }

# ICO container. Entries carry PNG payloads, which Windows has accepted since Vista and
# which keeps the file small compared with packing raw bitmaps.
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out

$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $bytes = $images[$i]

    # A dimension of 0 in the directory entry means 256, the largest a single byte can say.
    $declared = [Byte]$(if ($size -ge 256) { 0 } else { $size })

    $writer.Write($declared)
    $writer.Write($declared)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$bytes.Length)
    $writer.Write([UInt32]$offset)

    $offset += $bytes.Length
}

foreach ($bytes in $images) { $writer.Write([byte[]]$bytes, 0, $bytes.Length) }
$writer.Flush()

$resolved = [System.IO.Path]::GetFullPath($OutputPath)
$directory = [System.IO.Path]::GetDirectoryName($resolved)
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

[System.IO.File]::WriteAllBytes($resolved, $out.ToArray())
$writer.Dispose()

Write-Output "Wrote $resolved ($((Get-Item $resolved).Length) bytes, $($sizes.Count) sizes)"
