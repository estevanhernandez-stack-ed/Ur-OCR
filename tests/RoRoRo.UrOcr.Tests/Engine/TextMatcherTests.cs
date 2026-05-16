using Xunit;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using RoRoRo.UrOcr.Tests.Fixtures;

namespace RoRoRo.UrOcr.Tests.Engine;

public class TextMatcherTests
{
    [Fact]
    public void Matches_pure_logic_contains()
    {
        Assert.True(TextMatcher.Matches("hello BOSS world",
            new TextCriteria("boss", false, TextMatchType.Contains)));
        Assert.False(TextMatcher.Matches("nothing here",
            new TextCriteria("boss", false, TextMatchType.Contains)));
    }

    [Fact]
    public void Matches_case_sensitive_contains()
    {
        Assert.True(TextMatcher.Matches("BOSS",
            new TextCriteria("BOSS", true, TextMatchType.Contains)));
        Assert.False(TextMatcher.Matches("boss",
            new TextCriteria("BOSS", true, TextMatchType.Contains)));
    }

    [Fact]
    public void Matches_regex()
    {
        Assert.True(TextMatcher.Matches("level 42",
            new TextCriteria(@"level \d+", false, TextMatchType.Regex)));
    }

    [Fact]
    public async Task RunAsync_on_clean_banner_extracts_text()
    {
        var m = new TextMatcher();
        if (!m.IsAvailable) return;
        using var bmp = FixtureGenerator.MakeBanner("BOSS SPAWNED");
        var (matched, text) = await m.RunAsync(bmp,
            new TextCriteria("BOSS", false, TextMatchType.Contains));
        Assert.True(matched, $"OCR returned: '{text}'");
    }
}
