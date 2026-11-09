using System;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using Xunit;

namespace Jellyfin.MediaEncoding.Hls.Tests.OrderedChapters;

/// <summary>
/// Tests for the pure ordered-chapter input planner.
/// </summary>
public class OrderedChapterInputPlannerTests
{
    private static readonly Guid MainId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void TryCreate_RequestInFirstPart_UsesFirstSliceAndPositiveOffset()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 10_000_000L, false),
            new OrderedTimelinePart(MainId, "/b.mkv", 10_000_000L, 20_000_000L, false)
        };

        var plan = OrderedChapterInputPlanner.TryCreate(parts, 5_000_000L);

        Assert.NotNull(plan);
        Assert.Equal(5_000_000L, plan!.OutputTsOffsetTicks);
        Assert.Equal(2, plan.Inputs.Count);
        Assert.Equal("/a.mkv", plan.Inputs[0].Path);
        Assert.Equal(5_000_000L, plan.Inputs[0].SourceStartTicks);
        Assert.Equal(10_000_000L, plan.Inputs[0].SourceEndTicks);
        Assert.Contains("concat=n=2:v=1:a=1[ocv][oca]", plan.FilterComplexGraph, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreate_RequestInLastPart_UsesLastSliceAndOffsetWithinRange()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 10_000_000L, false),
            new OrderedTimelinePart(MainId, "/b.mkv", 10_000_000L, 20_000_000L, false)
        };

        var plan = OrderedChapterInputPlanner.TryCreate(parts, 15_000_000L);

        Assert.NotNull(plan);
        Assert.Equal(5_000_000L, plan!.OutputTsOffsetTicks);
        Assert.Equal("/b.mkv", plan.Inputs[0].Path);
        Assert.Equal(15_000_000L, plan.Inputs[0].SourceStartTicks);
    }

    [Fact]
    public void TryCreate_RequestOnBoundary_StartsAtNextPart()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 10_000_000L, false),
            new OrderedTimelinePart(MainId, "/b.mkv", 10_000_000L, 20_000_000L, false)
        };

        var plan = OrderedChapterInputPlanner.TryCreate(parts, 10_000_000L);

        Assert.NotNull(plan);
        Assert.Equal(0L, plan!.OutputTsOffsetTicks);
        Assert.Equal("/b.mkv", plan.Inputs[0].Path);
        Assert.Equal(10_000_000L, plan.Inputs[0].SourceStartTicks);
    }

    [Fact]
    public void TryCreate_MergedContiguousParts_UsesOnlyPlayableRanges()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 5_000_000L, false),
            new OrderedTimelinePart(MainId, "/a.mkv", 5_000_000L, 10_000_000L, false),
            new OrderedTimelinePart(MainId, "/b.mkv", 10_000_000L, 15_000_000L, false),
            new OrderedTimelinePart(MainId, "/c.mkv", 30_000_000L, 35_000_000L, true)
        };

        var plan = OrderedChapterInputPlanner.TryCreate(parts, 7_000_000L);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Inputs.Count);
        Assert.Equal("/a.mkv", plan.Inputs[0].Path);
        Assert.Equal("/b.mkv", plan.Inputs[1].Path);
        Assert.Equal(7_000_000L, plan.Inputs[0].SourceStartTicks);
        Assert.Equal(10_000_000L, plan.Inputs[1].SourceStartTicks);
    }

    [Fact]
    public void TryCreate_MissingPartProducesNoPlanWhenNothingPlayableExists()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 0L, true),
            new OrderedTimelinePart(MainId, "/b.mkv", 0L, 0L, true)
        };

        var plan = OrderedChapterInputPlanner.TryCreate(parts, 0L);

        Assert.Null(plan);
    }
}
