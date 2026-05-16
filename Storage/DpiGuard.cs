using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RoRoRo.UrOcr.Storage;

public sealed record DisplayFingerprint(int VirtualWidth, int VirtualHeight, int PrimaryDpi);

public enum DisplayCheckResult { Match, FirstRun, ChangedWithStaleRegions, ChangedRegionsStillInside }

public sealed class DpiGuard
{
    private readonly string _path;

    public DpiGuard() : this(PluginPaths.DisplayFingerprintFile) { }

    public DpiGuard(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public DisplayCheckResult Check(DisplayFingerprint current, IEnumerable<RegionRect> regions)
    {
        if (!File.Exists(_path))
        {
            Save(current);
            return DisplayCheckResult.FirstRun;
        }
        var saved = JsonSerializer.Deserialize<DisplayFingerprint>(
            File.ReadAllText(_path), TriggerJsonOptions.Default);
        if (saved == current) return DisplayCheckResult.Match;

        var stale = regions.Any(r =>
            r.X < 0 || r.Y < 0 ||
            r.X + r.Width > current.VirtualWidth ||
            r.Y + r.Height > current.VirtualHeight);
        Save(current);
        return stale ? DisplayCheckResult.ChangedWithStaleRegions
                     : DisplayCheckResult.ChangedRegionsStillInside;
    }

    private void Save(DisplayFingerprint fp)
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(fp, TriggerJsonOptions.Default));
        File.Move(tmp, _path, overwrite: true);
    }
}
