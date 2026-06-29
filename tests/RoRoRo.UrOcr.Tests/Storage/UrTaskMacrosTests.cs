using System;
using System.IO;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class UrTaskMacrosTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "urocr-macros-test-" + Guid.NewGuid().ToString("N"));

    public UrTaskMacrosTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Load_ReturnsEmpty_WhenDirDoesNotExist()
    {
        var result = UrTaskMacros.Load(Path.Combine(Path.GetTempPath(), "urocr-nonexistent-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(result);
    }

    [Fact]
    public void Load_ReturnsMacro_WhenValidJsonPresent()
    {
        var macroId = Guid.NewGuid().ToString();
        var json = $$"""{"schemaVersion":2,"id":"{{macroId}}","name":"jump-jump"}""";
        File.WriteAllText(Path.Combine(_tempDir, "jump.json"), json);

        var result = UrTaskMacros.Load(_tempDir);

        Assert.Single(result);
        Assert.Equal(macroId, result[0].Id);
        Assert.Equal("jump-jump", result[0].Name);
    }

    [Fact]
    public void Load_SkipsUnreadableFiles_AndContinues()
    {
        // Write one valid macro and one invalid file
        var macroId = Guid.NewGuid().ToString();
        File.WriteAllText(Path.Combine(_tempDir, "good.json"), $$"""{"id":"{{macroId}}","name":"ok"}""");
        File.WriteAllText(Path.Combine(_tempDir, "bad.json"), "not valid json {{{");

        var result = UrTaskMacros.Load(_tempDir);

        Assert.Single(result);
        Assert.Equal(macroId, result[0].Id);
    }

    [Fact]
    public void Load_UsesUnnamedFallback_WhenNameMissing()
    {
        var macroId = Guid.NewGuid().ToString();
        File.WriteAllText(Path.Combine(_tempDir, "noname.json"), $$"""{"id":"{{macroId}}"}""");

        var result = UrTaskMacros.Load(_tempDir);

        Assert.Single(result);
        Assert.Equal("(unnamed)", result[0].Name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }
}
