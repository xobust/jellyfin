using System;
using System.Collections.Generic;
using System.Reflection;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using IConfigurationManager = MediaBrowser.Common.Configuration.IConfigurationManager;

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
    public void TryCreatePlan_SingleFileParts_UsesFilterComplexConcatByDefault()
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
        Assert.True(plan.Segments.Count == 2);
        Assert.True(plan.ConcatDemuxerPath is null, "The optional demuxer fast path must stay gated behind aligned keyframes.");
    }

    [Fact]
    public void CanUseConcatDemuxerFastPath_WhenKeyframesAreAligned_ReturnsTrue()
    {
        var parts = new[]
        {
            new OrderedChapterTranscodeSegment("/a.mkv", 0L, 50_000_000L),
            new OrderedChapterTranscodeSegment("/a.mkv", 50_000_000L, 100_000_000L)
        };

        Assert.True(OrderedChapterTranscodePlanner.CanUseConcatDemuxerFastPath(parts, new[] { 0L, 50_000_000L, 100_000_000L }));
    }

    [Fact]
    public void TryCreatePlan_TwoPartsSameCodec_UsesFilterComplexConcat()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 50_000_000L, false),
            new OrderedTimelinePart(OtherId, "/b.mkv", 0L, 50_000_000L, false)
        };
        var timeline = new OrderedChapterTimelineResult(parts, 0, MainId, "/a.mkv");

        var plan = OrderedChapterTranscodePlanner.TryCreatePlan(MainId, timeline);

        Assert.NotNull(plan);
        Assert.Equal(OrderedChapterTranscodeMode.FilterComplexVideoConcat, plan!.Mode);
        Assert.True(plan.Segments.Count == 2);
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
        Assert.True(plan!.Segments.Count == 2);
    }

    [Fact]
    public void TryCreatePlan_WithRequestedVirtualTicks_UsesActiveSliceAndOffset()
    {
        var parts = new[]
        {
            new OrderedTimelinePart(MainId, "/a.mkv", 0L, 10_000_000L, false),
            new OrderedTimelinePart(MainId, "/b.mkv", 10_000_000L, 20_000_000L, false)
        };
        var timeline = new OrderedChapterTimelineResult(parts, 0, MainId, "/a.mkv");

        var plan = OrderedChapterTranscodePlanner.TryCreatePlan(MainId, timeline, 15_000_000L);

        Assert.NotNull(plan);
        Assert.Equal(5_000_000L, plan!.OutputTsOffsetTicks);
        Assert.True(plan.Segments.Count == 1);
        Assert.Equal("/b.mkv", plan.Segments[0].Path);
        Assert.Equal(15_000_000L, plan.Segments[0].StartTicks);
        Assert.Equal(20_000_000L, plan.Segments[0].EndTicks);
    }

    [Fact]
    public void ShouldAttemptOrderedChapterTranscode_ReturnsFalse_WhenSubtitleIsEncoded()
    {
        var helper = new EncodingHelper(
            Mock.Of<IApplicationPaths>(),
            Mock.Of<IMediaEncoder>(),
            Mock.Of<ISubtitleEncoder>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IConfigurationManager>(),
            Mock.Of<IPathManager>(),
            Mock.Of<IOrderedChapterTimelineBuilder>(),
            NullLogger<EncodingHelper>.Instance);

        var state = new EncodingJobInfo(TranscodingJobType.Hls)
        {
            HasOrderedChapters = true,
            IsInputVideo = true,
            InputContainer = "mkv",
            InputProtocol = MediaProtocol.File,
            MediaSource = new MediaSourceInfo { VideoType = VideoType.VideoFile },
            VideoStream = new MediaStream { Type = MediaStreamType.Video, Index = 0 },
            SubtitleStream = new MediaStream { Type = MediaStreamType.Subtitle, Index = 1, Codec = "srt" },
            SubtitleDeliveryMethod = SubtitleDeliveryMethod.Encode,
            BaseRequest = new BaseEncodingJobOptions(),
            LibraryItemId = MainId,
            LibraryItemPath = "/a.mkv"
        };

        var method = typeof(EncodingHelper).GetMethod("ShouldAttemptOrderedChapterTranscode", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var result = (bool)method.Invoke(helper, [state, "mkv"])!;

        Assert.False(result);
    }
}
