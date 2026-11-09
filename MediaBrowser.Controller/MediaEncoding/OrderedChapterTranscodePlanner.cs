using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Jellyfin.MediaEncoding.Keyframes;
using Jellyfin.MediaEncoding.Keyframes.Matroska;
using MediaBrowser.Controller.Streaming;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Builds ffmpeg input plans from <see cref="OrderedChapterTimelineResult"/>.
/// </summary>
public static class OrderedChapterTranscodePlanner
{
    /// <summary>
    /// Label used for concat filter output mapping.
    /// </summary>
    public const string VideoConcatOutputLabel = "[ocv]";

    /// <summary>
    /// Writes an ffconcat list file for the optional single-file demuxer fast path.
    /// </summary>
    /// <param name="segments">Segments to include in the output list.</param>
    /// <param name="outputPath">Absolute output path for the ffconcat file.</param>
    /// <returns>The output path that was written.</returns>
    public static string WriteConcatDemuxerFile(IReadOnlyList<OrderedChapterTranscodeSegment> segments, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A valid output path is required.", nameof(outputPath));
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sb = new StringBuilder();
        sb.AppendLine("ffconcat version 1.0");

        foreach (var segment in segments)
        {
            var escaped = segment.Path.Replace("'", "'\\''", StringComparison.Ordinal);
            sb.Append("file '")
                .Append(escaped)
                .AppendLine("'");

            sb.Append("inpoint ")
                .Append(segment.StartTicks / 10L)
                .AppendLine();

            sb.Append("outpoint ")
                .Append(segment.EndTicks / 10L)
                .AppendLine();
        }

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        return outputPath;
    }

    /// <summary>
    /// Tries to build a transcode plan. Returns null when the timeline does not require a multi-segment input path.
    /// </summary>
    /// <param name="mainItemId">Primary library item id.</param>
    /// <param name="timeline">Timeline from <see cref="IOrderedChapterTimelineBuilder"/>.</param>
    /// <returns>A plan or null.</returns>
    public static OrderedChapterTranscodePlan? TryCreatePlan(
        Guid mainItemId,
        OrderedChapterTimelineResult timeline)
    {
        return TryCreatePlan(mainItemId, timeline, 0L);
    }

    /// <summary>
    /// Tries to build a transcode plan for a specific virtual playback seek point.
    /// </summary>
    /// <param name="mainItemId">Primary library item id.</param>
    /// <param name="timeline">Timeline from <see cref="IOrderedChapterTimelineBuilder"/>.</param>
    /// <param name="requestedVirtualTicks">Requested virtual playback tick (seek target).</param>
    /// <returns>A plan or null.</returns>
    public static OrderedChapterTranscodePlan? TryCreatePlan(
        Guid mainItemId,
        OrderedChapterTimelineResult timeline,
        long requestedVirtualTicks)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        var playable = timeline.Parts
            .Where(p => !p.IsMissing && p.SourceEndTicks > p.SourceStartTicks)
            .ToList();

        if (playable.Count == 0)
        {
            return null;
        }

        var needsOrdered = playable.Count > 1 || playable.Any(p => !p.ItemId.Equals(mainItemId));
        if (!needsOrdered)
        {
            return null;
        }

        var activePlan = OrderedChapterInputPlanner.TryCreate(playable, requestedVirtualTicks);
        var activeInputs = (activePlan is null
                ? playable.Select(p => new OrderedChapterInputPlanner.OrderedChapterInput(p.Path, p.SourceStartTicks, p.SourceEndTicks))
                : activePlan.Inputs)
            .ToList();

        var segments = activeInputs
            .Select(i => new OrderedChapterTranscodeSegment(i.Path, i.SourceStartTicks, i.SourceEndTicks))
            .ToList();

        if (segments.Count == 0)
        {
            return null;
        }

        var sameFile = segments
            .Select(s => s.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == 1;

        var useConcatDemuxer = sameFile && CanUseConcatDemuxerFastPath(segments);
        var concatPath = useConcatDemuxer
            ? WriteConcatDemuxerFile(segments, Path.Combine(Path.GetTempPath(), "jf-ordered-chapters-" + Guid.NewGuid().ToString("N") + ".ffconcat"))
            : null;

        var n = segments.Count;
        var concatInputs = new List<string>(n);
        for (var i = 0; i < n; i++)
        {
            concatInputs.Add(string.Format(CultureInfo.InvariantCulture, "[{0}:v:0]", i));
        }

        var concat = string.Join(string.Empty, concatInputs)
                     + string.Format(CultureInfo.InvariantCulture, "concat=n={0}:v=1:a=0", n)
                     + VideoConcatOutputLabel;

        return new OrderedChapterTranscodePlan
        {
            Mode = useConcatDemuxer ? OrderedChapterTranscodeMode.ConcatDemuxer : OrderedChapterTranscodeMode.FilterComplexVideoConcat,
            Segments = segments,
            ConcatDemuxerPath = concatPath,
            FilterComplexGraph = useConcatDemuxer ? null : concat,
            VideoMapLabel = useConcatDemuxer ? null : VideoConcatOutputLabel,
            OutputTsOffsetTicks = activePlan?.OutputTsOffsetTicks ?? 0L
        };
    }

    /// <summary>
    /// Determines whether the optional single-file concat-demuxer fast path is safe for the given segments.
    /// The path is only allowed when each cut is aligned to a real video keyframe within one frame, which keeps
    /// ffmpeg packet jitter to a near-zero audio offset and avoids the incorrect default demuxer behavior.
    /// </summary>
    /// <param name="segments">Segments to validate.</param>
    /// <returns>True when the concat demuxer stream-copy path is safe to use.</returns>
    public static bool CanUseConcatDemuxerFastPath(IReadOnlyList<OrderedChapterTranscodeSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count == 0)
        {
            return false;
        }

        var distinctPaths = segments.Select(s => s.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinctPaths.Count != 1)
        {
            return false;
        }

        var keyframeTicks = GetKeyframeTicks(distinctPaths[0]);
        return CanUseConcatDemuxerFastPath(segments, keyframeTicks);
    }

    /// <summary>
    /// Determines whether the optional single-file concat-demuxer fast path is safe for the given segments using
    /// explicitly supplied keyframe ticks.
    /// </summary>
    /// <param name="segments">Segments to validate.</param>
    /// <param name="keyframeTicks">Expected keyframe ticks for the source file.</param>
    /// <returns>True when the concat demuxer stream-copy path is safe to use.</returns>
    public static bool CanUseConcatDemuxerFastPath(IReadOnlyList<OrderedChapterTranscodeSegment> segments, IReadOnlyList<long> keyframeTicks)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(keyframeTicks);

        if (segments.Count == 0 || keyframeTicks.Count == 0)
        {
            return false;
        }

        var maxFrameTicks = GetNearestKeyframeFrameTolerance(keyframeTicks);
        foreach (var segment in segments)
        {
            if (!IsWithinOneFrame(segment.StartTicks, keyframeTicks, maxFrameTicks)
                || !IsWithinOneFrame(segment.EndTicks, keyframeTicks, maxFrameTicks))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<long> GetKeyframeTicks(string path)
    {
        try
        {
            return MatroskaKeyframeExtractor.GetKeyframeData(path).KeyframeTicks;
        }
        catch (Exception)
        {
            return Array.Empty<long>();
        }
    }

    private static bool IsWithinOneFrame(long tick, IReadOnlyList<long> keyframeTicks, long toleranceTicks)
    {
        var nearest = keyframeTicks.Aggregate(
            keyframeTicks[0],
            (best, current) => Math.Abs(tick - best) <= Math.Abs(tick - current) ? best : current);

        return Math.Abs(tick - nearest) <= toleranceTicks;
    }

    private static long GetNearestKeyframeFrameTolerance(IReadOnlyList<long> keyframeTicks)
    {
        if (keyframeTicks.Count < 2)
        {
            return 1_000_000L;
        }

        var frameTicks = new List<long>();
        for (var i = 1; i < keyframeTicks.Count; i++)
        {
            var delta = keyframeTicks[i] - keyframeTicks[i - 1];
            if (delta > 0)
            {
                frameTicks.Add(delta);
            }
        }

        if (frameTicks.Count == 0)
        {
            return 1_000_000L;
        }

        return Math.Max(1L, frameTicks.Min());
    }

    /// <summary>
    /// Builds the exact ffmpeg input plan for a requested virtual timeline tick.
    /// </summary>
    /// <param name="parts">Playable ordered timeline parts in virtual order.</param>
    /// <param name="requestedVirtualTicks">Requested offset on the virtual timeline.</param>
    /// <returns>A pure planning result or null if no playable range is available.</returns>
    public static OrderedChapterInputPlanner.OrderedChapterInputPlan? TryCreateInputPlan(
        IReadOnlyList<OrderedTimelinePart> parts,
        long requestedVirtualTicks)
    {
        return OrderedChapterInputPlanner.TryCreate(parts, requestedVirtualTicks);
    }
}
