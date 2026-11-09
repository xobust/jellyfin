using System;
using System.Collections.Generic;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Planned inputs for ordered-chapter transcoding.
/// </summary>
public sealed class OrderedChapterTranscodePlan
{
    /// <summary>
    /// Gets or sets the transcode mode.
    /// </summary>
    public OrderedChapterTranscodeMode Mode { get; set; }

    /// <summary>
    /// Gets or sets playable segments (missing parts excluded).
    /// </summary>
    public IReadOnlyList<OrderedChapterTranscodeSegment> Segments { get; set; } = Array.Empty<OrderedChapterTranscodeSegment>();

    /// <summary>
    /// Gets or sets the temporary ffconcat list path when <see cref="Mode"/> is <see cref="OrderedChapterTranscodeMode.ConcatDemuxer"/>.
    /// </summary>
    public string? ConcatDemuxerPath { get; set; }

    /// <summary>
    /// Gets or sets the <c>-filter_complex</c> graph (without outer quotes) when <see cref="Mode"/> is <see cref="OrderedChapterTranscodeMode.FilterComplexVideoConcat"/>.
    /// </summary>
    public string? FilterComplexGraph { get; set; }

    /// <summary>
    /// Gets or sets the video output label for <c>-map</c>, e.g. <c>[ocv]</c>.
    /// </summary>
    public string? VideoMapLabel { get; set; }

    /// <summary>
    /// Gets or sets the virtual timestamp offset to apply to the output when the request starts in the middle of the active part.
    /// </summary>
    public long OutputTsOffsetTicks { get; set; }
}
