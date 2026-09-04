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

# One of the three spots. Round or square, ring-and-core or solid when very small.
function Add-Spot {
    param($G, [single]$Cx, [single]$Cy, [single]$S, $Brush, [bool]$Round, [bool]$Simplify)

    $radius = if ($Round) { 0.5 } else { 0.30 }

    if ($Simplify) {
        $solid = New-RoundedRect -X ($Cx - $S / 2) -Y ($Cy - $S / 2) -W $S -H $S -R ($S * $radius)
        $G.FillPath($Brush, $solid); $solid.Dispose()
        return
    }

    $stroke = $S * 0.23
    $ko = New-RoundedRect -X ($Cx - $S / 2) -Y ($Cy - $S / 2) -W $S -H $S -R ($S * $radius)
    $ki = New-RoundedRect -X ($Cx - $S / 2 + $stroke) -Y ($Cy - $S / 2 + $stroke) `
        -W ($S - 2 * $stroke) -H ($S - 2 * $stroke) -R (($S - 2 * $stroke) * $radius)
    $ring = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ring.AddPath($ko, $false); $ring.AddPath($ki, $false)
    $ring.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
    $G.FillPath($Brush, $ring)
    $ring.Dispose(); $ko.Dispose(); $ki.Dispose()

    $core = $S * 0.32
    $cp = New-RoundedRect -X ($Cx - $core / 2) -Y ($Cy - $core / 2) -W $core -H $core -R ($core * $radius)
    $G.FillPath($Brush, $cp); $cp.Dispose()
}

function New-Icon {
    param([int]$Size, [string]$Variant)

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
    $amber = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, ([single]$Size)),
        $amberTop, $amberBot)
    $simplify = $Size -lt 28

    $hasFrame = $Variant -in @('U1', 'U2', 'U5')
    $round    = $Variant -eq 'U6'
    $pad      = if ($hasFrame) { $Size * 0.115 } else { $Size * 0.155 }
    $box      = $Size - (2 * $pad)

    if ($hasFrame) {
        $stroke = [Math]::Max(1.0, $box * 0.085)
        $fr = $box * 0.12
        $outer = New-RoundedRect -X $pad -Y $pad -W $box -H $box -R $fr
        $inner = New-RoundedRect -X ($pad + $stroke) -Y ($pad + $stroke) `
            -W ($box - 2 * $stroke) -H ($box - 2 * $stroke) -R ([Math]::Max(0, $fr - $stroke))
        $frame = New-Object System.Drawing.Drawing2D.GraphicsPath
        $frame.AddPath($outer, $false); $frame.AddPath($inner, $false)
        $frame.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate
        $g.FillPath($pb, $frame)
        $frame.Dispose(); $outer.Dispose(); $inner.Dispose()

        $cl = $pad + ($stroke / 2.0)
        $span = $box - $stroke
        $spot = $box * 0.30
        $gap = [Math]::Max(1.0, $stroke * 0.55)

        $corners = @(@($cl, $cl), @(($cl + $span), $cl), @($cl, ($cl + $span)))
        for ($i = 0; $i -lt 3; $i++) {
            $cx = $corners[$i][0]; $cy = $corners[$i][1]
            $cut = New-RoundedRect -X ($cx - $spot / 2 - $gap) -Y ($cy - $spot / 2 - $gap) `
                -W ($spot + 2 * $gap) -H ($spot + 2 * $gap) -R (($spot + 2 * $gap) * 0.28)
            $g.FillPath($ground, $cut); $cut.Dispose()

            $brush = if ($Variant -eq 'U5' -and $i -eq 2) { $amber } else { $pb }
            Add-Spot $g $cx $cy $spot $brush $false $simplify
        }
    }
    else {
        # No frame: the three spots carry the whole mark, so they can be much larger.
        $spot = $box * 0.42
        $half = $spot / 2
        $corners = @(
            @(($pad + $half), ($pad + $half)),
            @(($pad + $box - $half), ($pad + $half)),
            @(($pad + $half), ($pad + $box - $half))
        )
        for ($i = 0; $i -lt 3; $i++) {
            $brush = if ($Variant -eq 'U7' -and $i -eq 2) { $amber } else { $pb }
            Add-Spot $g $corners[$i][0] $corners[$i][1] $spot $brush $round $simplify
        }
    }

    # Optional data modules, hinting at the body of a code without cluttering it.
    if ($Variant -in @('U2', 'U4') -and -not $simplify) {
        $cell = $box * 0.098
        $cells = if ($hasFrame) {
            @(@(0,0), @(1,1), @(2,0), @(0,2), @(2,2), @(1,3), @(3,1))
        } else {
            @(@(0,0), @(1,1), @(2,0), @(0,2), @(2,2), @(1,3), @(3,1), @(3,3), @(2,4))
        }
        $originX = ($Size / 2.0) - ($cell * 2.0)
        $originY = ($Size / 2.0) - ($cell * 2.0)
        if (-not $hasFrame) { $originX = $Size * 0.50; $originY = $Size * 0.50 }

        foreach ($c in $cells) {
            $x = $originX + ($c[0] * $cell)
            $y = $originY + ($c[1] * $cell)
            $s = $cell * 0.74
            $p = New-RoundedRect -X $x -Y $y -W $s -H $s -R ($s * 0.28)
            $g.FillPath($pb, $p); $p.Dispose()
        }
    }

    $pb.Dispose(); $amber.Dispose(); $ground.Dispose(); $g.Dispose()
    return $bmp
}

$variants = @(
    @{ k = 'U1'; n = 'U1  frame + three spots, empty centre' },
    @{ k = 'U2'; n = 'U2  frame + three spots + a few data modules' },
    @{ k = 'U3'; n = 'U3  no frame, three large spots alone' },
    @{ k = 'U4'; n = 'U4  no frame, three large spots + modules' },
    @{ k = 'U5'; n = 'U5  frame + three spots, one in amber' },
    @{ k = 'U6'; n = 'U6  no frame, three ROUND spots (literal dots)' },
    @{ k = 'U7'; n = 'U7  no frame, three square spots, one amber' }
)

$out = (Join-Path $env:TEMP "trispot1.png")
$rowH = 190
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

$y = 14
foreach ($v in $variants) {
    $g.DrawString($v.n, $bold, $black, 16, $y)

    $big = New-Icon -Size 256 -Variant $v.k
    $g.DrawImage($big, 20, ($y + 22), 148, 148); $big.Dispose()

    $g.FillRectangle($whiteBrush, 186, ($y + 22), 236, 148)
    $x = 200
    foreach ($sz in @(64, 48, 32, 16)) {
        $b = New-Icon -Size $sz -Variant $v.k
        $g.DrawImage($b, $x, ($y + 66), $sz, $sz)
        $g.DrawString("$sz", $font, $grey, $x, ($y + 136))
        $x += $sz + 16; $b.Dispose()
    }
    $g.DrawString('actual size on white', $font, $grey, 200, ($y + 152))

    $g.FillRectangle($darkBrush, 440, ($y + 22), 236, 148)
    $x = 454
    foreach ($sz in @(64, 48, 32, 16)) {
        $b = New-Icon -Size $sz -Variant $v.k
        $g.DrawImage($b, $x, ($y + 66), $sz, $sz)
        $x += $sz + 16; $b.Dispose()
    }

    $b32 = New-Icon -Size 32 -Variant $v.k
    $g.DrawImage($b32, 700, ($y + 22), 148, 148)
    $g.DrawString('32px enlarged', $font, $grey, 700, ($y + 152))
    $b32.Dispose()

    $y += $rowH
}

$g.Dispose()
$canvas.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
Copy-Item $out "c:\tmp\trispot-icon-concepts.png" -Force
Write-Output "wrote c:\tmp\trispot-icon-concepts.png"
