using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class RingTrackerTests
{
    private static readonly Rgb Navy = new(30, 30, 90);
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Orange = new(240, 160, 40);
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static RingDefinition Ring(int minSpots = 3) => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { Navy }),
        new LayerDefinition("grey", new[] { Grey }),
    }, minSpots);

    /// <summary>Eight samples: the given colours in ring order, tolerance 20.</summary>
    private static IReadOnlyList<SpotSample> Samples(params Rgb[] colours) =>
        colours.Select((c, i) => new SpotSample(i, c, 20)).ToList();

    [Fact]
    public void Vote_counts_spots_within_tolerance_of_each_layers_rock()
    {
        var votes = RingTracker.Vote(Ring(), Samples(Navy, Navy, Navy, Grey, Grey, Orange, Orange, new Rgb(35, 32, 95)));

        Assert.Equal(4, votes["navy"]);   // the last sample is about 7.3 from navy
        Assert.Equal(2, votes["grey"]);
    }

    [Fact]
    public void The_layer_with_the_most_spots_is_current()
    {
        var (s, _) = new RingTracker().Update(Ring(), Samples(Grey, Grey, Grey, Grey, Navy, Navy, Orange, Orange), T0);

        Assert.Equal(RingStatus.OnLayer, s.Status);
        Assert.Equal("grey", s.Layer);
        Assert.Equal(4, s.Votes);
        Assert.Equal(8, s.Spots);
    }

    [Fact]
    public void Fewer_than_min_spots_is_no_layer()
    {
        var (s, _) = new RingTracker().Update(Ring(minSpots: 3), Samples(Navy, Navy, Orange, Orange, Orange, Orange, Orange, Orange), T0);

        Assert.Equal(RingStatus.NoLayer, s.Status);
        Assert.Null(s.Layer);
        Assert.Equal(2, s.Votes);
    }

    [Fact]
    public void Three_rock_spots_still_read_the_layer_in_a_ring_full_of_ore()
    {
        var (s, _) = new RingTracker().Update(Ring(minSpots: 3), Samples(Navy, Navy, Navy, Orange, Orange, Orange, Orange, Orange), T0);

        Assert.Equal("navy", s.Layer);
    }

    [Fact]
    public void A_tie_keeps_the_previous_layer()
    {
        var tracker = new RingTracker();
        tracker.Update(Ring(), Samples(Grey, Grey, Grey, Grey, Grey, Grey, Grey, Grey), T0);

        var (s, changed) = tracker.Update(Ring(), Samples(Navy, Navy, Navy, Navy, Grey, Grey, Grey, Grey), T0.AddSeconds(1));

        Assert.Equal("grey", s.Layer);
        Assert.False(changed);
    }

    [Fact]
    public void A_tie_with_no_previous_layer_takes_the_first_in_list_order()
    {
        var (s, _) = new RingTracker().Update(Ring(), Samples(Grey, Grey, Grey, Grey, Navy, Navy, Navy, Navy), T0);

        Assert.Equal("navy", s.Layer);
    }

    [Fact]
    public void Since_holds_while_the_layer_holds_and_resets_on_a_change()
    {
        var tracker = new RingTracker();
        var (a, changedA) = tracker.Update(Ring(), Samples(Navy, Navy, Navy, Navy, Navy, Navy, Navy, Navy), T0);
        var (b, changedB) = tracker.Update(Ring(), Samples(Navy, Navy, Navy, Navy, Navy, Navy, Orange, Orange), T0.AddSeconds(5));
        var (c, changedC) = tracker.Update(Ring(), Samples(Grey, Grey, Grey, Grey, Grey, Grey, Grey, Grey), T0.AddSeconds(9));

        Assert.True(changedA);
        Assert.Equal(T0, a.Since);
        Assert.False(changedB);
        Assert.Equal(T0, b.Since);
        Assert.Equal(6, b.Votes);
        Assert.True(changedC);
        Assert.Equal(T0.AddSeconds(9), c.Since);
        Assert.Equal("grey", tracker.Get("MINE8").Layer);   // ring ids ignore case
    }

    [Fact]
    public void No_samples_is_unknown_not_no_layer()
    {
        var tracker = new RingTracker();
        tracker.Update(Ring(), Samples(Navy, Navy, Navy, Navy, Navy, Navy, Navy, Navy), T0);

        var (s, changed) = tracker.Update(Ring(), Array.Empty<SpotSample>(), T0.AddSeconds(1));

        Assert.Equal(RingStatus.Unknown, s.Status);
        Assert.True(changed);
        Assert.Equal("ring not visible", s.Describe());
    }

    [Fact]
    public void An_unseen_ring_is_unknown_and_marking_it_unknown_changes_nothing()
    {
        var tracker = new RingTracker();

        Assert.Equal(RingState.Initial, tracker.Get("mine8"));
        var (_, changed) = tracker.MarkUnknown("mine8", T0);
        Assert.False(changed);
    }

    [Fact]
    public void Describe_says_the_layer_and_the_votes()
    {
        var (s, _) = new RingTracker().Update(Ring(), Samples(Navy, Navy, Navy, Navy, Navy, Navy, Navy, Orange), T0);

        Assert.Equal("layer navy (7 of 8 spots)", s.Describe());
    }
}
