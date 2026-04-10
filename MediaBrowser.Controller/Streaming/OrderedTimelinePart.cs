using System;

namespace MediaBrowser.Controller.Streaming;

/// <summary>
/// A contiguous slice of source media on the virtual ordered-chapter timeline.
/// </summary>
/// <param name="ItemId">Library item providing the file.</param>
/// <param name="Path">Path probed for this part.</param>
/// <param name="SourceStartTicks">Inclusive start in Matroska chapter time units (100 ns ticks, same as TimeSpan ticks).</param>
/// <param name="SourceEndTicks">Exclusive end in the same units.</param>
/// <param name="IsMissing">True when a segment link could not be resolved.</param>
public sealed record OrderedTimelinePart(
    Guid ItemId,
    string Path,
    long SourceStartTicks,
    long SourceEndTicks,
    bool IsMissing);
