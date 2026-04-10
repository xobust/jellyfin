using System;
using System.Collections.Generic;

namespace MediaBrowser.Controller.Streaming;

/// <summary>
/// Virtual timeline built from Matroska ordered chapters and segment links.
/// </summary>
/// <param name="Parts">Ordered playback slices on source files.</param>
/// <param name="MissingTicks">Sum of virtual durations (in timeline ticks) that could not be resolved.</param>
/// <param name="LayoutItemId">Item used for track layout (codec negotiation).</param>
/// <param name="LayoutPath">Path for <paramref name="LayoutItemId"/>.</param>
public sealed record OrderedChapterTimelineResult(
    IReadOnlyList<OrderedTimelinePart> Parts,
    long MissingTicks,
    Guid LayoutItemId,
    string LayoutPath);
