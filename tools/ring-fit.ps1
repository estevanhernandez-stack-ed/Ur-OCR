# Fits the ring's colours from labelled samples. Inputs: samples.csv from ring-sample.ps1, the
# measured-values JSON, and a labels.csv you write while looking at the ring-NNNN.png sheets:
#
#   frame,spot,kind,layer
#   12,E,ore,navy
#   12,*,rock,navy          (* = every spot of that frame not labelled on its own row)
#
# kind is rock, ore, empty (sky or a mined-out hole), self (the character), or skip.
# layer is the layer the frame shows; every row except skip needs one.
#
# For each layer it picks up to -MaxColours rock colours that cover the layer's rock samples within
# tolerance (greedy: the sample colour covering the most still-uncovered samples, then the next),
# and the same for the ignore list from empty and self samples. Then it checks the fit the way
# Ur OCR runs it, and lists:
#   MISSED ORE   an ore sample within tolerance of its layer's rock or the ignore list (never stopped for)
#   FALSE STOP   a rock, empty or self sample outside every chosen colour (a short needless mine)
#   WRONG LAYER  a frame whose votes pick another layer, or no layer
# With -Write it puts layers, ignore and toleranceRgb into the measured-values JSON.
#
# Zero labelled ore samples means MISSED ORE can never fire, so the fit is untested on the one
# thing this whole exercise exists to catch: the verdict says "fit untested for ore" (never "fit
# clean") and the script exits non-zero, alongside a WARNING line and the per-kind sample counts
# folded into the verdict.
#
#   pwsh -File tools\ring-fit.ps1 -Samples C:\sweep\samples.csv -Labels C:\sweep\labels.csv -Measured C:\sweep\mine8.measured.json
#   ... -Tolerance 25 -Write
param(
    [Parameter(Mandatory = $true)][string]$Samples,
    [Parameter(Mandatory = $true)][string]$Labels,
    [Parameter(Mandatory = $true)][string]$Measured,
    [int]$Tolerance = -1,
    [int]$MaxColours = 6,
    [switch]$Write
)

Add-Type -TypeDefinition @'
using System; using System.Collections.Generic;
public static class RingFit {
  public static double Dist(int[] a, int[] b) {
    double dr = a[0] - b[0], dg = a[1] - b[1], db = a[2] - b[2];
    return Math.Sqrt(dr * dr + dg * dg + db * db);
  }
  public static double Nearest(int[] c, List<int[]> set) {
    double best = double.PositiveInfinity;
    foreach (var s in set) { var d = Dist(c, s); if (d < best) best = d; }
    return best;
  }
  public static List<int[]> Cover(List<int[]> colours, double tol, int max) {
    var cands = new List<int[]>(); var seen = new HashSet<string>();
    foreach (var c in colours) if (seen.Add(c[0] + "," + c[1] + "," + c[2])) cands.Add(c);
    var uncovered = new List<int[]>(colours); var chosen = new List<int[]>();
    while (uncovered.Count > 0 && chosen.Count < max) {
      int[] best = null; int bestN = 0;
      foreach (var cand in cands) {
        int k = 0; foreach (var u in uncovered) if (Dist(cand, u) <= tol) k++;
        if (k > bestN) { bestN = k; best = cand; }
      }
      if (best == null) break;
      chosen.Add(best);
      var picked = best;
      uncovered.RemoveAll(u => Dist(picked, u) <= tol);
    }
    return chosen;
  }
}
'@

function Hex($c) { "#{0:X2}{1:X2}{2:X2}" -f $c[0], $c[1], $c[2] }

$m = Get-Content $Measured -Raw | ConvertFrom-Json
if ($Tolerance -lt 0) { $Tolerance = [int]$m.toleranceRgb }
$tol = [double]$Tolerance
$minSpots = [int]$m.minLayerSpots

$exact = @{}; $wild = @{}
foreach ($l in (Import-Csv $Labels)) {
    if ($l.spot -eq '*') { $wild[[int]$l.frame] = $l } else { $exact["$([int]$l.frame)|$($l.spot)"] = $l }
}
$rows = [System.Collections.Generic.List[object]]::new()
foreach ($s in (Import-Csv $Samples)) {
    $f = [int]$s.frame
    $l = $exact["$f|$($s.spot)"]
    if (-not $l) { $l = $wild[$f] }
    if (-not $l -or $l.kind -eq 'skip') { continue }
    if ($l.kind -notin @('rock', 'ore', 'empty', 'self')) { "BAD LABEL frame $f $($s.spot): kind '$($l.kind)'"; exit 2 }
    if (-not $l.layer) { "BAD LABEL frame $f $($s.spot): no layer"; exit 2 }
    $rows.Add([pscustomobject]@{ frame = $f; spot = $s.spot; kind = $l.kind; layer = $l.layer; c = [int[]]@([int]$s.r, [int]$s.g, [int]$s.b) })
}
if ($rows.Count -eq 0) { "NO LABELLED SAMPLES: check labels.csv against samples.csv"; exit 2 }

$layerNames = [System.Collections.Generic.List[string]]::new()
foreach ($r in $rows) { if (-not $layerNames.Contains($r.layer)) { $layerNames.Add($r.layer) } }

$ignoreIn = [System.Collections.Generic.List[int[]]]::new()
foreach ($r in $rows) { if ($r.kind -in @('empty', 'self')) { $ignoreIn.Add($r.c) } }
$ignore = [RingFit]::Cover($ignoreIn, $tol, $MaxColours)
"ignore: $($ignoreIn.Count) samples -> $($ignore.Count) colours: $((@($ignore | ForEach-Object { Hex $_ })) -join ' ')"

$fitted = @{}
foreach ($name in $layerNames) {
    $rockIn = [System.Collections.Generic.List[int[]]]::new()
    foreach ($r in $rows) { if ($r.layer -eq $name -and $r.kind -eq 'rock') { $rockIn.Add($r.c) } }
    $fitted[$name] = [RingFit]::Cover($rockIn, $tol, $MaxColours)
    "layer ${name}: $($rockIn.Count) rock samples -> $($fitted[$name].Count) colours: $((@($fitted[$name] | ForEach-Object { Hex $_ })) -join ' ')"
    if ($rockIn.Count -eq 0) { "WARNING: layer $name has no rock samples; Ur OCR will reject it" }
}

$rockCount = @($rows | Where-Object { $_.kind -eq 'rock' }).Count
$oreCount = @($rows | Where-Object { $_.kind -eq 'ore' }).Count
$ignoreCount = $ignoreIn.Count
if ($oreCount -eq 0) { "WARNING: 0 ore samples labelled; MISSED ORE not tested" }

$missed = 0; $falseStops = 0; $wrong = 0
foreach ($r in $rows) {
    $d = [math]::Min([RingFit]::Nearest($r.c, $fitted[$r.layer]), [RingFit]::Nearest($r.c, $ignore))
    if ($r.kind -eq 'ore') {
        if ($d -le $tol) { "MISSED ORE   frame {0} {1} {2} d={3:F1}" -f $r.frame, $r.spot, (Hex $r.c), $d; $missed++ }
    } elseif ($d -gt $tol) {
        "FALSE STOP   frame {0} {1} {2} ({3}) d={4:F1}" -f $r.frame, $r.spot, (Hex $r.c), $r.kind, $d; $falseStops++
    }
}

foreach ($group in ($rows | Group-Object frame)) {
    $labelled = $group.Group[0].layer
    $best = $null; $bestVotes = -1; $line = @()
    foreach ($name in $layerNames) {
        $v = 0
        foreach ($r in $group.Group) { if ([RingFit]::Nearest($r.c, $fitted[$name]) -le $tol) { $v++ } }
        $line += "$name $v"
        if ($v -gt $bestVotes) { $bestVotes = $v; $best = $name }
    }
    if ($bestVotes -lt $minSpots) { $best = "no layer" }
    if ($best -ne $labelled) {
        "WRONG LAYER  frame {0}: labelled {1}, reads {2} ({3})" -f $group.Name, $labelled, $best, ($line -join ', ')
        $wrong++
    }
}

$verdict = if ($oreCount -eq 0) { "fit untested for ore" } elseif ($missed + $falseStops + $wrong -eq 0) { "fit clean" } else { "fit has problems" }
"{0}: {1} missed ore, {2} false stops, {3} wrong layers (tolerance {4}, {5} samples: {6} rock, {7} ore, {8} ignore)" -f $verdict, $missed, $falseStops, $wrong, $Tolerance, $rows.Count, $rockCount, $oreCount, $ignoreCount

if ($Write) {
    $toJson = { param($list) @(foreach ($c in $list) { [pscustomobject]@{ r = $c[0]; g = $c[1]; b = $c[2] } }) }
    $layersOut = @(foreach ($name in $layerNames) { [pscustomobject]@{ name = $name; rock = @(& $toJson $fitted[$name]) } })
    $m | Add-Member -Force -NotePropertyName layers -NotePropertyValue $layersOut
    $m | Add-Member -Force -NotePropertyName ignore -NotePropertyValue @(& $toJson $ignore)
    $m | Add-Member -Force -NotePropertyName toleranceRgb -NotePropertyValue $Tolerance
    $m | ConvertTo-Json -Depth 8 | Set-Content -Path $Measured -Encoding utf8
    "wrote $($layerNames.Count) layers and $($ignore.Count) ignore colours to $Measured"
}

if ($oreCount -eq 0) { exit 1 }
