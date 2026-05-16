using Xunit;
using RoRoRo.UrOcr.Engine;

namespace RoRoRo.UrOcr.Tests.Engine;

public class ActivityLogTests
{
    [Fact]
    public void Records_in_newest_first_order()
    {
        var log = new ActivityLog(10);
        var id = Guid.NewGuid();
        log.Record(id, "T", ActivityKind.NoMatch);
        log.Record(id, "T", ActivityKind.Fired);
        var snap = log.Snapshot();
        Assert.Equal(ActivityKind.Fired, snap[0].Kind);
        Assert.Equal(ActivityKind.NoMatch, snap[1].Kind);
    }

    [Fact]
    public void Caps_at_capacity()
    {
        var log = new ActivityLog(3);
        var id = Guid.NewGuid();
        for (int i = 0; i < 10; i++) log.Record(id, "T", ActivityKind.NoMatch, i.ToString());
        var snap = log.Snapshot();
        Assert.Equal(3, snap.Count);
        Assert.Equal("9", snap[0].Detail);
        Assert.Equal("7", snap[2].Detail);
    }

    [Fact]
    public void Raises_Changed_event()
    {
        var log = new ActivityLog();
        int fired = 0;
        log.Changed += () => fired++;
        log.Record(Guid.NewGuid(), "T", ActivityKind.Fired);
        Assert.Equal(1, fired);
    }
}
