#!/usr/bin/env pwsh
# Generates the Ur OCR plugin icon.
# Run from the plugin root: pwsh ./build/generate-icon.ps1

Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

# Transparent background — Format32bppArgb defaults to fully transparent

# Colors
$cyan = [System.Drawing.Color]::FromArgb(255, 0x17, 0xD4, 0xFA)
$magenta = [System.Drawing.Color]::FromArgb(255, 0xF2, 0x2F, 0x89)

# ===== Hex frame =====
# Regular hexagon centered, flat-top orientation, ~210px max width
# nudge center up slightly to leave room for swoosh below
$cx = [float]($size / 2)
$cy = [float]($size / 2 - 8)
$hexRadius = [float]102      # distance from center to vertex; fits comfortably with swoosh below
$hexPoints = [System.Collections.Generic.List[System.Drawing.PointF]]::new()
for ($i = 0; $i -lt 6; $i++) {
    $angleDeg = 60 * $i - 30      # -30 gives flat-top orientation
    $angleRad = $angleDeg * [Math]::PI / 180
    $px = [float]($cx + $hexRadius * [Math]::Cos($angleRad))
    $py = [float]($cy + $hexRadius * [Math]::Sin($angleRad))
    $hexPoints.Add([System.Drawing.PointF]::new($px, $py))
}
$hexPen = New-Object System.Drawing.Pen $cyan, 6
$hexPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$g.DrawPolygon($hexPen, [System.Drawing.PointF[]]$hexPoints.ToArray())
$hexPen.Dispose()

# ===== Viewfinder brackets (4 L-shaped corners) =====
$vfWidth  = [float]108
$vfHeight = [float]72
$vfLeft   = [float]($cx - $vfWidth / 2)
$vfTop    = [float]($cy - $vfHeight / 2 - 4)
$vfRight  = [float]($vfLeft + $vfWidth)
$vfBottom = [float]($vfTop + $vfHeight)
$bracketLen = [float]18

$bPen = New-Object System.Drawing.Pen $cyan, 3.5
$bPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$bPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

# Top-left
$g.DrawLine($bPen, $vfLeft, $vfTop + $bracketLen, $vfLeft, $vfTop)
$g.DrawLine($bPen, $vfLeft, $vfTop, $vfLeft + $bracketLen, $vfTop)
# Top-right
$g.DrawLine($bPen, $vfRight - $bracketLen, $vfTop, $vfRight, $vfTop)
$g.DrawLine($bPen, $vfRight, $vfTop, $vfRight, $vfTop + $bracketLen)
# Bottom-left
$g.DrawLine($bPen, $vfLeft, $vfBottom - $bracketLen, $vfLeft, $vfBottom)
$g.DrawLine($bPen, $vfLeft, $vfBottom, $vfLeft + $bracketLen, $vfBottom)
# Bottom-right
$g.DrawLine($bPen, $vfRight - $bracketLen, $vfBottom, $vfRight, $vfBottom)
$g.DrawLine($bPen, $vfRight, $vfBottom, $vfRight, $vfBottom - $bracketLen)
$bPen.Dispose()

# ===== Text lines inside viewfinder (cyan->magenta gradient bars) =====
# Three horizontal bars of varying widths to suggest lines of text being scanned
$barH   = [float]8
$barGap = [float]8
$barAreaH = 3 * $barH + 2 * $barGap
$y0 = [float]($vfTop + ($vfHeight - $barAreaH) / 2)

$barDefs = @(
    @{ xOff = 10; w = $vfWidth - 20 },  # full width line
    @{ xOff = 10; w = $vfWidth - 38 },  # shorter line
    @{ xOff = 10; w = $vfWidth - 28 }   # medium line
)

for ($i = 0; $i -lt 3; $i++) {
    $bd = $barDefs[$i]
    $barX = [float]($vfLeft + $bd.xOff)
    $barY = [float]($y0 + $i * ($barH + $barGap))
    $barW = [float]$bd.w

    # Per-bar gradient so each bar transitions cyan->magenta across its own width
    $pt1 = [System.Drawing.PointF]::new($barX, $barY)
    $pt2 = [System.Drawing.PointF]::new($barX + $barW, $barY)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
        $pt1, $pt2, $cyan, $magenta
    $rect = [System.Drawing.RectangleF]::new($barX, $barY, $barW, $barH)
    $g.FillRectangle($grad, $rect)
    $grad.Dispose()
}

# ===== Swoosh arcs underneath the hex (two crossing bezier curves) =====
# Positioned so they clear the hex bottom (~$cy + $hexRadius) and sit within canvas
$hexBottom = [float]($cy + $hexRadius)   # approx 110 + 102 = ~212 with nudge

$swooshPen1 = New-Object System.Drawing.Pen $cyan, 5
$swooshPen1.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$swooshPen1.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

$swooshPen2 = New-Object System.Drawing.Pen $magenta, 5
$swooshPen2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$swooshPen2.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

# Cyan arc: sweeps from lower-left of hex to lower-right, dips below
$g.DrawBezier($swooshPen1,
    [System.Drawing.PointF]::new([float]34,  [float]218),
    [System.Drawing.PointF]::new([float]82,  [float]248),
    [System.Drawing.PointF]::new([float]176, [float]240),
    [System.Drawing.PointF]::new([float]222, [float]218))

# Magenta arc: same endpoints, curves the other direction (crosses cyan)
$g.DrawBezier($swooshPen2,
    [System.Drawing.PointF]::new([float]34,  [float]236),
    [System.Drawing.PointF]::new([float]82,  [float]210),
    [System.Drawing.PointF]::new([float]176, [float]222),
    [System.Drawing.PointF]::new([float]222, [float]236))

$swooshPen1.Dispose()
$swooshPen2.Dispose()

# Save
$root    = Split-Path -Parent $PSScriptRoot
$outPath = Join-Path $root "icon.png"
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

Write-Host "Wrote: $outPath ($size x $size)" -ForegroundColor Green
