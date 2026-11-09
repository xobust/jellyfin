using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        var segments = (activePlan is null ? playable : activePlan.Inputs)
            .Select(i => new OrderedChapterTranscodeSegment(i.Path, i.SourceStartTicks, i.SourceEndTicks))
            .ToList();

        if (segments.Count == 0)
        {
            return null;
        }

        var n = segments.Count;
        var inputs = new List<string>(n);
        for (var i = 0; i < n; i++)
        {
            inputs.Add(string.Format(CultureInfo.InvariantCulture, "[{0}:v:0]", i));
        }

        var concat = string.Join(string.Empty, inputs)
                     + string.Format(CultureInfo.InvariantCulture, "concat=n={0}:v=1:a=0", n)
                     + VideoConcatOutputLabel;

        return new OrderedChapterTranscodePlan
        {
            Mode = OrderedChapterTranscodeMode.FilterComplexVideoConcat,
            Segments = segments,
            FilterComplexGraph = concat,
            VideoMapLabel = VideoConcatOutputLabel,
            OutputTsOffsetTicks = activePlan?.OutputTsOffsetTicks ?? 0L
        };
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
