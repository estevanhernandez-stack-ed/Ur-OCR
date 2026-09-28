# Ring sweep: saves the foreground Roblox window's game area (client area, pixel for pixel, no
# grid) every few seconds, for measuring the ore-stop ring. Pair with ring-sample.ps1.
#
# Read-only: never activates, moves, resizes, or clicks anything. A tick is skipped (and says so)
# whenever the foreground window is not Roblox, so tabbing away is harmless.
#
#   pwsh -File tools\ring-sweep.ps1 -OutDir C:\sweep                   every 3 s for 15 minutes
#   pwsh -File tools\ring-sweep.ps1 -OutDir C:\sweep -Seconds 2 -Minutes 30
#   pwsh -File tools\ring-sweep.ps1 -OutDir C:\sweep -Demo             two synthetic 800x599 frames
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [double]$Seconds = 3,
    [double]$Minutes = 15,
    [switch]$Demo
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Sweep {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
}
'@
[void][Sweep]::SetProcessDpiAwarenessContext([IntPtr](-4))

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$index = Join-Path $OutDir "frames.csv"
if (-not (Test-Path $index)) { "frame,time,file,w,h,scale" | Set-Content -Path $index -Encoding utf8 }
$n = @(Import-Csv $index).Count

function Save-Frame($bmp, [int]$scale) {
    $script:n++
    $name = "frame-{0:0000}.png" -f $script:n
    $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    "{0},{1},{2},{3},{4},{5}" -f $script:n, (Get-Date -Format "HH:mm:ss"), $name, $bmp.Width, $bmp.Height, $scale |
        Add-Content -Path $index -Encoding utf8
    "frame $($script:n): $name $($bmp.Width)x$($bmp.Height) at $scale%"
}

if ($Demo) {
    # Frame 1: navy rock, an orange ore block east of the character. Frame 2: grey rock all round.
    # The character is a white block at (400, 300); the demo spots sit 40 px from it.
    foreach ($spec in @(@{ rock = @(30, 30, 90); ore = $true }, @{ rock = @(120, 120, 120); ore = $false })) {
        $bmp = New-Object System.Drawing.Bitmap 800, 599
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb($spec.rock[0], $spec.rock[1], $spec.rock[2]))
        $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(250, 250, 250))), 390, 290, 20, 20)
        if ($spec.ore) {
            $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 160, 40))), 430, 290, 20, 20)
        }
        $g.Dispose()
        Save-Frame $bmp 100
        $bmp.Dispose()
    }
    exit 0
}

$end = (Get-Date).AddMinutes($Minutes)
while ((Get-Date) -lt $end) {
    $h = [Sweep]::GetForegroundWindow()
    $procId = [uint32]0
    [void][Sweep]::GetWindowThreadProcessId($h, [ref]$procId)
    $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if (-not $proc -or $proc.ProcessName -ne "RobloxPlayerBeta") {
        "skip: the foreground window is $(if ($proc) { $proc.ProcessName } else { 'nothing' }), not Roblox"
    } else {
        $cr = New-Object Sweep+RECT; [void][Sweep]::GetClientRect($h, [ref]$cr)
        $pt = New-Object Sweep+POINT; [void][Sweep]::ClientToScreen($h, [ref]$pt)
        if ($cr.r -gt 100 -and $cr.b -gt 100) {
            $bmp = New-Object System.Drawing.Bitmap $cr.r, $cr.b
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.CopyFromScreen($pt.x, $pt.y, 0, 0, (New-Object System.Drawing.Size $cr.r, $cr.b))
            $g.Dispose()
            Save-Frame $bmp ([math]::Round([Sweep]::GetDpiForWindow($h) / 96.0 * 100))
            $bmp.Dispose()
        }
    }
    Start-Sleep -Milliseconds ([int]($Seconds * 1000))
}
