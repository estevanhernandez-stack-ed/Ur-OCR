# For each frame from ring-sweep.ps1, averages every ring spot's sample box (the same box, clamped
# the same way, as Ur OCR's ColorMatcher.AverageBox) and writes samples.csv next to the frames.
# Also writes ring-NNNN.png per frame: the eight spot crops, enlarged, laid out around the
# character (centre tile), with the sample box outlined and the hex printed, for labelling by eye.
#
#   pwsh -File tools\ring-sample.ps1 -Frames C:\sweep -Measured C:\sweep\mine8.measured.json
param(
    [Parameter(Mandatory = $true)][string]$Frames,
    [Parameter(Mandatory = $true)][string]$Measured,
    [int]$Crop = 24,
    [int]$Zoom = 4
)

Add-Type -AssemblyName System.Drawing

$m = Get-Content $Measured -Raw | ConvertFrom-Json
$index = Join-Path $Frames "frames.csv"
if (-not (Test-Path $index)) { "NO frames.csv in ${Frames}: run ring-sweep.ps1 first"; exit 2 }
if (@($m.spots).Count -ne 8) { "the measured file lists $(@($m.spots).Count) spots, not 8"; exit 2 }
$box = $m.box
$rw = [int]$m.recordedClientW; $rh = [int]$m.recordedClientH

function Average-Box($bmp, [int]$px, [int]$py) {
    $x0 = [math]::Min([math]::Max($px + [int]$box.offsetX, 0), $bmp.Width - 1)
    $y0 = [math]::Min([math]::Max($py + [int]$box.offsetY, 0), $bmp.Height - 1)
    $x1 = [math]::Min([math]::Max($px + [int]$box.offsetX + [int]$box.w, $x0 + 1), $bmp.Width)
    $y1 = [math]::Min([math]::Max($py + [int]$box.offsetY + [int]$box.h, $y0 + 1), $bmp.Height)
    $r = 0; $gg = 0; $b = 0; $count = 0
    for ($y = $y0; $y -lt $y1; $y++) {
        for ($x = $x0; $x -lt $x1; $x++) {
            $c = $bmp.GetPixel($x, $y); $r += $c.R; $gg += $c.G; $b += $c.B; $count++
        }
    }
    # Integer division, as Ur OCR's AverageRect does.
    return @([int][math]::Floor($r / $count), [int][math]::Floor($gg / $count), [int][math]::Floor($b / $count))
}

$cells = @{ N = @(1, 0); NE = @(2, 0); E = @(2, 1); SE = @(2, 2); S = @(1, 2); SW = @(0, 2); W = @(0, 1); NW = @(0, 0) }
$tile = $Crop * $Zoom
$strip = 18
$font = New-Object System.Drawing.Font "Consolas", 9, ([System.Drawing.FontStyle]::Bold)
$white = [System.Drawing.Brushes]::White
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 220, 0)), 1

function Draw-Crop($g, $bmp, [int]$px, [int]$py, [int]$col, [int]$row, [string]$label) {
    $half = [int]($Crop / 2)
    $src = New-Object System.Drawing.Rectangle ($px - $half), ($py - $half), $Crop, $Crop
    $x = $col * $tile; $y = $row * ($tile + $strip)
    $dst = New-Object System.Drawing.Rectangle $x, $y, $tile, $tile
    $g.DrawImage($bmp, $dst, $src, [System.Drawing.GraphicsUnit]::Pixel)
    $bx = $x + ($half + [int]$box.offsetX) * $Zoom
    $by = $y + ($half + [int]$box.offsetY) * $Zoom
    $g.DrawRectangle($pen, $bx, $by, [int]$box.w * $Zoom - 1, [int]$box.h * $Zoom - 1)
    $g.DrawString($label, $font, $white, [float]($x + 2), [float]($y + $tile + 2))
}

$out = Join-Path $Frames "samples.csv"
"frame,time,spot,order,x,y,r,g,b,hex" | Set-Content -Path $out -Encoding utf8
$sheets = 0
foreach ($f in (Import-Csv $index)) {
    $bmp = [System.Drawing.Bitmap]::FromFile((Join-Path $Frames $f.file))
    if ($bmp.Width -ne $rw -or $bmp.Height -ne $rh) {
        "note: frame $($f.frame) is $($bmp.Width)x$($bmp.Height); spots scaled from ${rw}x${rh}"
    }
    $sx = $bmp.Width / $rw; $sy = $bmp.Height / $rh
    $sheet = New-Object System.Drawing.Bitmap (3 * $tile), (3 * ($tile + $strip))
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::Black)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $cx = 0; $cy = 0
    foreach ($s in $m.spots) {
        $px = [int][math]::Round($s.x * $sx); $py = [int][math]::Round($s.y * $sy)
        $cx += $px; $cy += $py
        $avg = Average-Box $bmp $px $py
        $hex = "#{0:X2}{1:X2}{2:X2}" -f $avg[0], $avg[1], $avg[2]
        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}" -f $f.frame, $f.time, $s.name, $s.order, $px, $py, $avg[0], $avg[1], $avg[2], $hex |
            Add-Content -Path $out -Encoding utf8
        $cell = $cells[[string]$s.name]
        Draw-Crop $g $bmp $px $py $cell[0] $cell[1] "$($s.name) $hex"
    }
    Draw-Crop $g $bmp ([int]($cx / 8)) ([int]($cy / 8)) 1 1 "self"
    $g.Dispose()
    $sheet.Save((Join-Path $Frames ("ring-{0:0000}.png" -f [int]$f.frame)), [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose(); $bmp.Dispose()
    $sheets++
}
"wrote $out and $sheets ring sheets"
