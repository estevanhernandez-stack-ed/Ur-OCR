// tests/RoRoRo.UrOcr.Tests/Ipc/BridgeContractTests.cs
using System.IO;
using System.Text;
using System.Text.Json;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Ipc;

public class BridgeContractTests
{
    [Fact]
    public void ForMacro_BuildsValidRequest_CamelCase()
    {
        var req = BridgeContract.ForMacro("f4e5d6c7-0000-0000-0000-000000000000", null);
        Assert.Equal("1.0", req.ContractVersion);
        Assert.Equal("RunMacro", req.Method);
        Assert.Equal("626labs.ur-ocr", req.CallerPluginId);
        Assert.Equal(new[] { "foreground" }, req.Targets);  // null targets => ["foreground"]
        var json = JsonSerializer.Serialize(req, BridgeContract.Json);
        Assert.Contains("\"contractVersion\":\"1.0\"", json);
        Assert.Contains("\"callerPluginId\":\"626labs.ur-ocr\"", json);
    }

    [Fact]
    public async Task Frame_RoundTrips()
    {
        var payload = Encoding.UTF8.GetBytes("{\"x\":1}");
        using var ms = new MemoryStream();
        await FrameCodec.WriteFrameAsync(ms, payload, default);
        ms.Position = 0;
        Assert.Equal(payload, await FrameCodec.ReadFrameAsync(ms, default));
    }

    [Fact]
    public async Task ReadFrame_EmptyStream_ReturnsNull()
    {
        using var ms = new MemoryStream();
        Assert.Null(await FrameCodec.ReadFrameAsync(ms, default));
    }
}
