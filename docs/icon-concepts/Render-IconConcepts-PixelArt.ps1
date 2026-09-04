Add-Type -AssemblyName System.Drawing

$navyTop = [System.Drawing.Color]::FromArgb(255, 42, 60, 96)
$navyBot = [System.Drawing.Color]::FromArgb(255, 20, 32, 58)
$paper   = [System.Drawing.Color]::FromArgb(255, 250, 251, 253)

# Anvils authored as pixel art rather than as vector outlines.
#
# Every previous attempt described the anvil as a polygon and hoped it survived being
# scaled. Authoring it cell by cell puts the silhouette under direct control at the scale
# it is actually seen, which is what pixel art is for. It also rhymes with the subject:
# the mark is built from modules, like the codes the app makes.
$Anvils = @{
    # The base is deliberately NARROWER than the face and sits under its right half. A base
    # as wide as the face is what made the earlier attempts read as an I-beam. The horn
    # steps out to the left over three rows to a single-cell point.
    'T1' = @(
        '.....###########'
        '...#############'
        '.###############'
        '################'
        '.###############'
        '...#############'
        '.........#####..'
        '.........#####..'
        '.........#####..'
        '........#######.'
        '.......#########'
        '.......#########'
    )
    # Deeper face and a slightly wider foot.
    'T2' = @(
        '.....###########'
        '...#############'
        '.###############'
        '################'
        '.###############'
        '...#############'
        '.....###########'
        '.........#####..'
        '.........#####..'
        '........#######.'
        '......##########'
        '......##########'
    )
    # Fewer, larger cells so the silhouette survives further down the sizes.
    'T3' = @(
        '....#########'
        '..###########'
        '#############'
        '..###########'
        '....#########'
        '.......####..'
        '.......####..'
        '......######.'
        '.....########'
        '.....########'
    )
}

function New-RoundedRect {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)
    $r = [Math]::Min([Math]::Max($R, 0), [Math]::Min($W, $H) / 2.0)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0.01) { $p.AddRectangle((New-Object System.Drawing.RectangleF $X, $Y, $W, $H)); return $p }
    $d = $r * 2
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc(($X + $W - $d), $Y, $d, $d, 270, 90)
    $p.AddArc(($X + $W - $d), ($Y + $H - $d), $d, $d, 0, 90)
    $p.AddArc($X, ($Y + $H - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Icon {
    param([int]$Size, [string]$Art, [single]$Fill, [single]$GapFrac)

    $rows = $Anvils[$Art]
    $cols = ($rows | Measure-Object -Property Length -Maximum).Maximum
    $rowCount = $rows.Count

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $ground = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, ([single]$Size)),
        $navyTop, $navyBot)
    $tile = New-RoundedRect -X 0 -Y 0 -W $Size -H $Size -R ($Size * 0.225)
    $g.FillPath($ground, $tile); $tile.Dispose()

    $pb = New-Object System.Drawing.SolidBrush $paper
    $simplify = $Size -lt 32

    $pad = $Size * 0.115
    $box = $Size - (2 * $pad)
    $stroke = [Math]::Max(1.0, $box * 0.085)
    $frameRadius = $box * 0.12

    $outer = New-RoundedRect -X $pad -Y $pad -W $box -H $box -R $frameRadius
    $inner = New-RoundedRect -X ($pad + $stroke) -Y ($pad + $stroke) `
        -W ($box - 2 * $stroke) -H ($box - 2 * $stroke) -R ([Math]::Max(0, $frameRadius - $stroke))
    $frame = New-Object System.Drawing.Drawing2D.GraphicsPath
    $frame.AddPath($outer, $false); $frame.AddPath($inner, $false)
    $frame.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
    $g.FillPath($pb, $frame)
    $frame.Dispose(); $outer.Dispose(); $inner.Dispose()

    $cl = $pad + ($stroke / 2.0)
    $clSpan = $box - $stroke
    $k = $box * 0.30
    $gap = [Math]::Max(1.0, $stroke * 0.55)

    foreach ($c in @(@($cl, $cl), @(($cl + $clSpan), $cl), @($cl, ($cl + $clSpan)))) {
        $cx = $c[0]; $cy = $c[1]
        $cut = New-RoundedRect -X ($cx - $k / 2 - $gap) -Y ($cy - $k / 2 - $gap) `
            -W ($k + 2 * $gap) -H ($k + 2 * $gap) -R (($k + 2 * $gap) * 0.28)
        $g.FillPath($ground, $cut); $cut.Dispose()

        if ($simplify) {
            $solid = New-RoundedRect -X ($cx - $k / 2) -Y ($cy - $k / 2) -W $k -H $k -R ($k * 0.28)
            $g.FillPath($pb, $solid); $solid.Dispose()
        }
        else {
            $ks = $k * 0.24
            $ko = New-RoundedRect -X ($cx - $k / 2) -Y ($cy - $k / 2) -W $k -H $k -R ($k * 0.30)
            $ki = New-RoundedRect -X ($cx - $k / 2 + $ks) -Y ($cy - $k / 2 + $ks) `
                -W ($k - 2 * $ks) -H ($k - 2 * $ks) -R (($k - 2 * $ks) * 0.26)
            $ring = New-Object System.Drawing.Drawing2D.GraphicsPath
            $ring.AddPath($ko, $false); $ring.AddPath($ki, $false)
            $ring.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
            $g.FillPath($pb, $ring); $ring.Dispose(); $ko.Dispose(); $ki.Dispose()
            $core = $k * 0.30
            $cp = New-RoundedRect -X ($cx - $core / 2) -Y ($cy - $core / 2) -W $core -H $core -R ($core * 0.30)
            $g.FillPath($pb, $cp); $cp.Dispose()
        }
    }

    # Cell size rounded to whole pixels and the block centred, so the art lands on the
    # pixel grid instead of smearing across it. That crispness is the whole point.
    $available = $box * $Fill
    $cell = [Math]::Floor($available / $cols)
    if ($cell -lt 1) { $cell = $available / $cols }   # below ~24px there is no room to snap

    $artW = $cell * $cols
    $artH = $cell * $rowCount
    $originX = [Math]::Round((($Size - $artW) / 2.0))
    $originY = [Math]::Round((($Size - $artH) / 2.0))

    $inset = $cell * $GapFrac
    $draw = $cell - $inset

    $g.SmoothingMode = if ($GapFrac -gt 0) { [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias }
                       else { [System.Drawing.Drawing2D.SmoothingMode]::None }

    for ($r = 0; $r -lt $rowCount; $r++) {
        $line = $rows[$r]
        for ($c2 = 0; $c2 -lt $line.Length; $c2++) {
            if ($line[$c2] -ne '#') { continue }

            $x = $originX + ($c2 * $cell)
            $y = $originY + ($r * $cell)

            if ($GapFrac -gt 0) {
                $p = New-RoundedRect -X $x -Y $y -W $draw -H $draw -R ($draw * 0.22)
                $g.FillPath($pb, $p); $p.Dispose()
            }
            else {
                $g.FillRectangle($pb, $x, $y, $cell, $cell)
            }
        }
    }

    $pb.Dispose(); $ground.Dispose(); $g.Dispose()
    return $bmp
}

$variants = @(
    @{ n = 'T1  narrow base under the right half, long stepped horn'; a = 'T1'; f = 0.68; gp = 0.00 },
    @{ n = 'T2  deeper face, slightly wider foot';                    a = 'T2'; f = 0.68; gp = 0.00 },
    @{ n = 'T3  fewer, larger cells';                                 a = 'T3'; f = 0.66; gp = 0.00 }
)

$out = (Join-Path $env:TEMP "pixelart2.png")
$rowH = 200
$canvas = New-Object System.Drawing.Bitmap 900, ($variants.Count * $rowH + 30)
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.Clear([System.Drawing.Color]::FromArgb(255, 242, 243, 246))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

$font = New-Object System.Drawing.Font 'Segoe UI', 9
$bold = New-Object System.Drawing.Font 'Segoe UI', 11, ([System.Drawing.FontStyle]::Bold)
$black = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Black)
$grey = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 110, 118, 130))
$darkBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
$whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

$y = 16
foreach ($v in $variants) {
    $g.DrawString($v.n, $bold, $black, 16, $y)

    $big = New-Icon -Size 256 -Art $v.a -Fill $v.f -GapFrac $v.gp
    $g.DrawImage($big, 20, ($y + 24), 158, 158); $big.Dispose()

    $g.FillRectangle($whiteBrush, 196, ($y + 24), 236, 158)
    $x = 210
    foreach ($sz in @(64, 48, 32, 16)) {
        $b = New-Icon -Size $sz -Art $v.a -Fill $v.f -GapFrac $v.gp
        $g.DrawImage($b, $x, ($y + 76), $sz, $sz)
        $g.DrawString("$sz", $font, $grey, $x, ($y + 146))
        $x += $sz + 16; $b.Dispose()
    }
    $g.DrawString('actual size on white', $font, $grey, 210, ($y + 164))

    $g.FillRectangle($darkBrush, 450, ($y + 24), 236, 158)
    $x = 464
    foreach ($sz in @(64, 48, 32, 16)) {
        $b = New-Icon -Size $sz -Art $v.a -Fill $v.f -GapFrac $v.gp
        $g.DrawImage($b, $x, ($y + 76), $sz, $sz)
        $x += $sz + 16; $b.Dispose()
    }

    $b48 = New-Icon -Size 48 -Art $v.a -Fill $v.f -GapFrac $v.gp
    $g.DrawImage($b48, 706, ($y + 24), 158, 158)
    $g.DrawString('48px enlarged', $font, $grey, 706, ($y + 164))
    $b48.Dispose()

    $y += $rowH
}

$g.Dispose()
$canvas.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
Copy-Item $out "c:\tmp\TrispotQR-icon-pixelart2.png" -Force
Write-Output "wrote c:\tmp\TrispotQR-icon-pixelart2.png"
