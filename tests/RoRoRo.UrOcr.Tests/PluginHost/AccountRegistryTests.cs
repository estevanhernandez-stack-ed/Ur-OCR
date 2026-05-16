using Xunit;
using RoRoRo.UrOcr.PluginHost;

namespace RoRoRo.UrOcr.Tests.PluginHost;

public class AccountRegistryTests
{
    [Fact]
    public void Add_then_TryGet_returns_userId()
    {
        var r = new AccountRegistry();
        r.Add(1234, 5678L);
        Assert.True(r.TryGetUserId(1234, out var uid));
        Assert.Equal(5678L, uid);
        Assert.True(r.IsAltPid(1234));
    }

    [Fact]
    public void Remove_drops_pid()
    {
        var r = new AccountRegistry();
        r.Add(1234, 5678L);
        r.Remove(1234);
        Assert.False(r.IsAltPid(1234));
        Assert.False(r.TryGetUserId(1234, out _));
    }

    [Fact]
    public void Unknown_pid_returns_false()
    {
        var r = new AccountRegistry();
        Assert.False(r.IsAltPid(9999));
    }
}
