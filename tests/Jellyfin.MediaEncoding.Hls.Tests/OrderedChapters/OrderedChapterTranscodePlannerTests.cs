using System;
using System.Collections.Generic;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using Xunit;

namespace Jellyfin.MediaEncoding.Hls.Tests.OrderedChapters;

/// <summary>
/// Tests for <see cref="OrderedChapterTranscodePlanner"/>.
/// </summary>
public class OrderedChapterTranscodePlannerTests
{
    private static readonly Guid MainId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void TryCreatePlan_SingleLocalPart_ReturnsNull()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 100_000_000L, false)
        };
        var timeline = new OrderedChapterTimelineResult(parts, 0, MainId, "/a.mkv");

        var plan = OrderedChapterTranscodePlanner.TryCreatePlan(MainId, timeline);

        Assert.Null(plan);
    }

    [Fact]
    public void TryCreatePlan_TwoPartsSameCodec_UsesFilterComplexConcat()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 50_000_000L, false),
            new OrderedTimelinePart(MainId, "/a.mkv", 50_000_000L, 100_000_000L, false)
        };
        var timeline = new OrderedChapterTimelineResult(parts, 0, MainId, "/a.mkv");

        var plan = OrderedChapterTranscodePlanner.TryCreatePlan(MainId, timeline);

        Assert.NotNull(plan);
        Assert.Equal(OrderedChapterTranscodeMode.FilterComplexVideoConcat, plan!.Mode);
        Assert.Equal(2, plan.Segments.Count);
        Assert.Contains("concat=n=2:v=1:a=0", plan.FilterComplexGraph, StringComparison.Ordinal);
        Assert.Equal(OrderedChapterTranscodePlanner.VideoConcatOutputLabel, plan.VideoMapLabel);
    }

    [Fact]
    public void TryCreatePlan_CodecMismatch_UsesFilterComplexConcat()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 50_000_000L, false),
            new OrderedTimelinePart(OtherId, "/b.mkv", 0L, 50_000_000L, false)
        };
        var timeline = new OrderedChapterTimelineResult(parts, 0, MainId, "/a.mkv");
        var codecs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/a.mkv"] = "h264",
            ["/b.mkv"] = "hevc"
        };

        var plan = OrderedChapterTranscodePlanner.TryCreatePlan(MainId, timeline);

        Assert.NotNull(plan);
        Assert.Equal(OrderedChapterTranscodeMode.FilterComplexVideoConcat, plan!.Mode);
        Assert.Contains("concat=n=2:v=1:a=0", plan.FilterComplexGraph, StringComparison.Ordinal);
        Assert.Equal(OrderedChapterTranscodePlanner.VideoConcatOutputLabel, plan.VideoMapLabel);
    }

    [Fact]
    public void TryCreatePlan_MissingPartsExcluded_StillPlansPlayableSegments()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 0L, true),
            new OrderedTimelinePart(OtherId, "/b.mkv", 0L, 40_000_000L, false),
            new OrderedTimelinePart(OtherId, "/c.mkv", 0L, 40_000_000L, false)
        };
        var timeline = new OrderedChapterTimelineResult(parts, 99_000_000L, OtherId, "/b.mkv");
        var codecs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/b.mkv"] = "h264",
            ["/c.mkv"] = "h264"
        };

        var plan = OrderedChapterTranscodePlanner.TryCreatePlan(MainId, timeline);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Segments.Count);
    }
}
