using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using RoRoRo.UrOcr.Storage;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace RoRoRo.UrOcr.Engine;

public sealed class TextMatcher : ITextMatchEngine
{
    private readonly OcrEngine? _engine = OcrEngine.TryCreateFromUserProfileLanguages();
    public bool IsAvailable => _engine is not null;

    public async Task<(bool matched, string text)> RunAsync(Bitmap bmp, TextCriteria c)
    {
        if (_engine is null) return (false, string.Empty);
        var soft = await ToSoftwareBitmapAsync(bmp);
        var result = await _engine.RecognizeAsync(soft);
        var text = string.Join(' ', result.Lines.Select(l => l.Text));
        return (Matches(text, c), text);
    }

    public async Task<(bool matched, string text)> RunWithPreprocessAsync(Bitmap bmp, TextCriteria c)
    {
        var (matched, text) = await RunAsync(bmp, c);
        if (matched) return (true, text);
        using var pre = ImagePreprocessor.Preprocess(bmp);
        return await RunAsync(pre, c);
    }

    public static bool Matches(string text, TextCriteria c)
    {
        var haystack = c.CaseSensitive ? text : text.ToLowerInvariant();
        var needle = c.CaseSensitive ? c.Needle : c.Needle.ToLowerInvariant();
        return c.MatchType switch
        {
            TextMatchType.Contains => haystack.Contains(needle, StringComparison.Ordinal),
            TextMatchType.Exact => haystack.Trim() == needle.Trim(),
            TextMatchType.Regex => Regex.IsMatch(haystack, needle,
                c.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
            _ => false
        };
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        using var ras = new InMemoryRandomAccessStream();
        var writer = new DataWriter(ras);
        writer.WriteBytes(ms.ToArray());
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        ras.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ras);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
