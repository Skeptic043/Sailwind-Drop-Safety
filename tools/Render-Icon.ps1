# Draw the package icon from simple, editable shapes: a fishing rod falling
# overboard, crossed out. Writes icon.png (256x256) in the repository root.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
function Color($hex) { [Drawing.ColorTranslator]::FromHtml($hex) }
$navy = Color '#1C3441'; $paper = Color '#E8D3A8'; $gold = Color '#A98A5D'; $ink = Color '#594631'; $light = Color '#F3E3BF'

$disposables = New-Object System.Collections.Generic.List[IDisposable]
function Track($obj) { $disposables.Add($obj); $obj }
function Pen($color, [float]$width) {
    $pen = Track ([Drawing.Pen]::new($color, $width))
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $pen
}
function Brush($color) { Track ([Drawing.SolidBrush]::new($color)) }
function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = Track ([Drawing.Drawing2D.GraphicsPath]::new())
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $path.AddArc($x, $y, $d, $d, 180, 90); $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure(); $path
}

$large = Track ([Drawing.Bitmap]::new(1024, 1024))
$small = Track ([Drawing.Bitmap]::new(256, 256))
$g = Track ([Drawing.Graphics]::FromImage($large))
$smallGraphics = Track ([Drawing.Graphics]::FromImage($small))
try {
    $g.Clear($navy)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform(4, 4)

    # Water: two rows of gentle swells.
    foreach ($row in 0, 1) {
        $y = 192 + $row * 12; $x = 66 + $row * 8; $end = 190 - $row * 8; $up = $true
        $wave = Track ([Drawing.Drawing2D.GraphicsPath]::new())
        while ($x + 20 -le $end) {
            $offset = $(if ($up) { -5 } else { 5 })
            $wave.AddBezier($x, $y, $x + 7, $y + $offset, $x + 13, $y + $offset, $x + 20, $y)
            $x += 20; $up = -not $up
        }
        $g.DrawPath((Pen $gold 5), $wave)
    }

    # Falling rod through (cx, cy), tip to the upper right.
    $cx = 126; $cy = 132; $scale = 0.78; $angle = 66
    $rad = $angle * [Math]::PI / 180
    $dx = [Math]::Sin($rad); $dy = -[Math]::Cos($rad)

    # Speed lines trail straight up from the shaft.
    foreach ($along in -30, 20, 70) {
        $x = $cx + $dx * $along * $scale; $y = $cy + $dy * $along * $scale
        $g.DrawLine((Pen $light 4.5), $x, $y - 12, $x, $y - 38)
    }

    # Line and bobber float up behind the tip.
    $tipX = $cx + $dx * 110 * $scale; $tipY = $cy + $dy * 110 * $scale
    $g.DrawBezier((Pen $light 2), $tipX, $tipY, $tipX - 6, $tipY - 26, $tipX - 30, $tipY - 22, $tipX - 34, $tipY - 48)
    $g.FillEllipse((Brush $light), $tipX - 39, $tipY - 60, 10, 13)
    $g.FillRectangle((Brush $ink), $tipX - 39, $tipY - 54, 10, 3)

    # Rod: shaft, grip, reel and reel handle.
    $state = $g.Save()
    $g.TranslateTransform($cx, $cy); $g.RotateTransform($angle); $g.ScaleTransform($scale, $scale)
    $g.DrawLine((Pen $paper 10), 0, -110, 0, 66)
    $g.FillPath((Brush $ink), (RoundRect -7 50 14 50 7))
    $g.FillEllipse((Brush $ink), -34, 34, 28, 28)
    $g.FillEllipse((Brush $gold), -26, 42, 12, 12)
    $g.DrawLine((Pen $ink 5), -20, 48, -40, 40)
    $g.Restore($state)

    # Cancel sign over everything.
    $g.DrawEllipse((Pen $gold 11), 22, 22, 212, 212)
    $g.DrawLine((Pen $gold 11), 54, 54, 202, 202)

    $smallGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $smallGraphics.DrawImage($large, [Drawing.Rectangle]::new(0, 0, 256, 256))
    $small.Save((Join-Path $root 'icon.png'), [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    for ($i = $disposables.Count - 1; $i -ge 0; $i--) { $disposables[$i].Dispose() }
}
Write-Host "Wrote $(Join-Path $root 'icon.png')"
