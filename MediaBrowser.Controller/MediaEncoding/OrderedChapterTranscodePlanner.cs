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
        ArgumentNullException.ThrowIfNull(timeline);

        var paired = timeline.Parts
            .Where(p => !p.IsMissing && p.SourceEndTicks > p.SourceStartTicks)
            .ToList();

        if (paired.Count == 0)
        {
            return null;
        }

        var needsOrdered = paired.Count > 1 || paired.Any(p => !p.ItemId.Equals(mainItemId));
        if (!needsOrdered)
        {
            return null;
        }

        var segments = paired
            .Select(p => new OrderedChapterTranscodeSegment(p.Path, p.SourceStartTicks, p.SourceEndTicks))
            .ToList();

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
            VideoMapLabel = VideoConcatOutputLabel
        };
    }
}
