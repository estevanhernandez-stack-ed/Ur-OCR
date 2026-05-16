using RoRoRo.UrOcr.PluginHost;
using Xunit;

namespace RoRoRo.UrOcr.Tests.PluginHost;

public class ElevationProbeTests
{
    [Fact]
    public void Zero_pid_returns_false()
    {
        Assert.False(new ElevationProbe().IsForegroundProcessLikelyElevated(0));
    }

    [Fact]
    public void Own_pid_returns_false()
    {
        var probe = new ElevationProbe();
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        Assert.False(probe.IsForegroundProcessLikelyElevated(pid));
    }
}
