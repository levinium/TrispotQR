Add-Type -AssemblyName System.Drawing

$navyTop  = [System.Drawing.Color]::FromArgb(255, 42, 60, 96)
$navyBot  = [System.Drawing.Color]::FromArgb(255, 20, 32, 58)
$paper    = [System.Drawing.Color]::FromArgb(255, 250, 251, 253)
$amberTop = [System.Drawing.Color]::FromArgb(255, 255, 190, 92)
$amberBot = [System.Drawing.Color]::FromArgb(255, 240, 138, 32)

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

function New-AnvilPath {
    param([single]$X, [single]$Y, [single]$S)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    function Pt { param($a, $b) New-Object System.Drawing.PointF (($X + $a / 100.0 * $S), ($Y + $b / 100.0 * $S)) }
    $p.AddLine((Pt 24 26), (Pt 86 26))
    $p.AddBezier((Pt 86 26), (Pt 91 26), (Pt 93 28), (Pt 93 33))
    $p.AddLine((Pt 93 33), (Pt 93 39))
    $p.AddBezier((Pt 93 39), (Pt 93 43), (Pt 91 45), (Pt 87 45))
    $p.AddLine((Pt 87 45), (Pt 64 45))
    $p.AddBezier((Pt 64 45), (Pt 59 54), (Pt 57 58), (Pt 57 66))
    $p.AddBezier((Pt 57 66), (Pt 57 73), (Pt 64 76), (Pt 72 78))
    $p.AddBezier((Pt 72 78), (Pt 77 79), (Pt 79 81), (Pt 79 85))
    $p.AddLine((Pt 79 85), (Pt 79 88))
    $p.AddBezier((Pt 79 88), (Pt 79 91), (Pt 77 92), (Pt 74 92))
    $p.AddLine((Pt 74 92), (Pt 26 92))
    $p.AddBezier((Pt 26 92), (Pt 23 92), (Pt 21 91), (Pt 21 88))
    $p.AddLine((Pt 21 88), (Pt 21 85))
    $p.AddBezier((Pt 21 85), (Pt 21 81), (Pt 23 79), (Pt 28 78))
    $p.AddBezier((Pt 28 78), (Pt 36 76), (Pt 43 73), (Pt 43 66))
    $p.AddBezier((Pt 43 66), (Pt 43 58), (Pt 41 54), (Pt 36 45))
    $p.AddLine((Pt 36 45), (Pt 24 45))
    $p.AddBezier((Pt 24 45), (Pt 16 44), (Pt 10 42), (Pt 5 38))
    $p.AddBezier((Pt 5 38), (Pt 2 36), (Pt 2 33), (Pt 6 31))
    $p.AddBezier((Pt 6 31), (Pt 11 28), (Pt 17 26), (Pt 24 26))
    $p.CloseFigure()
    return $p
}

function New-Icon {
    param(
        [int]$Size,
        [single]$FrameR,     # frame corner radius as a fraction of the frame box
        [single]$KeyFrac,    # key square size as a fraction of the frame box
        [single]$AnvilFrac,
        [bool]$Billet
    )

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Kept as a live brush: the key squares punch a gap in the frame by repainting the
    # exact background gradient, so the gap matches the ground perfectly at any position.
    $ground = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, ([single]$Size)),
        $navyTop, $navyBot)

    $tile = New-RoundedRect -X 0 -Y 0 -W $Size -H $Size -R ($Size * 0.225)
    $g.FillPath($ground, $tile)
    $tile.Dispose()

    $pb = New-Object System.Drawing.SolidBrush $paper
    $simplify = $Size -lt 32

    $pad = $Size * 0.115
    $box = $Size - (2 * $pad)
    $stroke = [Math]::Max(1.0, $box * 0.085)
    $frameRadius = $box * $FrameR

    $outer = New-RoundedRect -X $pad -Y $pad -W $box -H $box -R $frameRadius
    $inner = New-RoundedRect -X ($pad + $stroke) -Y ($pad + $stroke) `
        -W ($box - 2 * $stroke) -H ($box - 2 * $stroke) -R ([Math]::Max(0, $frameRadius - $stroke))
    $frame = New-Object System.Drawing.Drawing2D.GraphicsPath
    $frame.AddPath($outer, $false); $frame.AddPath($inner, $false)
    $frame.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
    $g.FillPath($pb, $frame)
    $frame.Dispose(); $outer.Dispose(); $inner.Dispose()

    # The frame's centreline. Key squares are centred on its corners, so the border runs
    # straight through the middle of each one.
    $cl = $pad + ($stroke / 2.0)
    $clSpan = $box - $stroke

    $k = $box * $KeyFrac
    $gap = [Math]::Max(1.0, $stroke * 0.55)

    $corners = @(
        @($cl, $cl),
        @(($cl + $clSpan), $cl),
        @($cl, ($cl + $clSpan))
    )

    foreach ($c in $corners) {
        $cx = $c[0]; $cy = $c[1]

        # 1. Repaint the ground in a slightly larger square, cutting the frame away so the
        #    key square is not white sitting on white.
        $cut = New-RoundedRect -X ($cx - $k / 2 - $gap) -Y ($cy - $k / 2 - $gap) `
            -W ($k + 2 * $gap) -H ($k + 2 * $gap) -R (($k + 2 * $gap) * 0.28)
        $g.FillPath($ground, $cut)
        $cut.Dispose()

        # 2. The key square itself, fully opaque on top.
        if ($simplify) {
            $solid = New-RoundedRect -X ($cx - $k / 2) -Y ($cy - $k / 2) -W $k -H $k -R ($k * 0.28)
            $g.FillPath($pb, $solid)
            $solid.Dispose()
        }
        else {
            $ks = $k * 0.24
            $ko = New-RoundedRect -X ($cx - $k / 2) -Y ($cy - $k / 2) -W $k -H $k -R ($k * 0.30)
            $ki = New-RoundedRect -X ($cx - $k / 2 + $ks) -Y ($cy - $k / 2 + $ks) `
                -W ($k - 2 * $ks) -H ($k - 2 * $ks) -R (($k - 2 * $ks) * 0.26)
            $ring = New-Object System.Drawing.Drawing2D.GraphicsPath
            $ring.AddPath($ko, $false); $ring.AddPath($ki, $false)
            $ring.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
            $g.FillPath($pb, $ring)
            $ring.Dispose(); $ko.Dispose(); $ki.Dispose()

            $core = $k * 0.30
            $cp = New-RoundedRect -X ($cx - $core / 2) -Y ($cy - $core / 2) -W $core -H $core -R ($core * 0.30)
            $g.FillPath($pb, $cp)
            $cp.Dispose()
        }
    }

    $s = ($box * $AnvilFrac) / 0.91
    $anvilX = ($Size / 2.0) - (0.475 * $s)
    $anvilY = ($Size * 0.575) - (0.59 * $s)
    $anvil = New-AnvilPath -X $anvilX -Y $anvilY -S $s
    $g.FillPath($pb, $anvil)
    $anvil.Dispose()

    if ($Billet -and -not $simplify) {
        $bs = $box * 0.155
        $bx = $anvilX + (0.585 * $s) - ($bs / 2.0)
        $by = $anvilY + (0.26 * $s) - $bs
        $amber = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF 0, ([single]$by)),
            (New-Object System.Drawing.PointF 0, ([single]($by + $bs))), $amberTop, $amberBot)
        $billetPath = New-RoundedRect -X $bx -Y $by -W $bs -H $bs -R ($bs * 0.28)
        $g.FillPath($amber, $billetPath)
        $billetPath.Dispose(); $amber.Dispose()
    }

    $pb.Dispose(); $ground.Dispose(); $g.Dispose()
    return $bmp
}

$variants = @(
    @{ n = 'N1  square frame corners, key squares centred on them'; r = 0.00; k = 0.30; a = 0.62; b = $false },
    @{ n = 'N2  softly rounded frame, key squares on the corners';  r = 0.12; k = 0.30; a = 0.62; b = $false },
    @{ n = 'N3  N1 with the hot billet on the anvil face';          r = 0.00; k = 0.30; a = 0.62; b = $true }
)

$out = (Join-Path $env:TEMP "concepts7.png")
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

    $big = New-Icon -Size 256 -FrameR $v.r -KeyFrac $v.k -AnvilFrac $v.a -Billet $v.b
    $g.DrawImage($big, 20, ($y + 24), 158, 158); $big.Dispose()

    $g.FillRectangle($whiteBrush, 196, ($y + 24), 236, 158)
    $x = 210
    foreach ($s in @(64, 48, 32, 16)) {
        $b = New-Icon -Size $s -FrameR $v.r -KeyFrac $v.k -AnvilFrac $v.a -Billet $v.b
        $g.DrawImage($b, $x, ($y + 76), $s, $s)
        $g.DrawString("$s", $font, $grey, $x, ($y + 146))
        $x += $s + 16; $b.Dispose()
    }
    $g.DrawString('actual size on white', $font, $grey, 210, ($y + 164))

    $g.FillRectangle($darkBrush, 450, ($y + 24), 236, 158)
    $x = 464
    foreach ($s in @(64, 48, 32, 16)) {
        $b = New-Icon -Size $s -FrameR $v.r -KeyFrac $v.k -AnvilFrac $v.a -Billet $v.b
        $g.DrawImage($b, $x, ($y + 76), $s, $s)
        $x += $s + 16; $b.Dispose()
    }

    $b32 = New-Icon -Size 32 -FrameR $v.r -KeyFrac $v.k -AnvilFrac $v.a -Billet $v.b
    $g.DrawImage($b32, 706, ($y + 24), 158, 158)
    $g.DrawString('32px enlarged', $font, $grey, 706, ($y + 164))
    $b32.Dispose()

    $y += $rowH
}

$g.Dispose()
$canvas.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
Copy-Item $out "c:\tmp\TrispotQR-icon-concepts-v7.png" -Force
Write-Output "wrote c:\tmp\TrispotQR-icon-concepts-v7.png"
