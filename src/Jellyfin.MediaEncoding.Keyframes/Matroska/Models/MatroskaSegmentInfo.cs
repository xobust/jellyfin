using System.Collections.Generic;

namespace Jellyfin.MediaEncoding.Keyframes.Matroska.Models;

/// <summary>
/// Parsed Matroska Info segment identity plus chapter editions.
/// </summary>
/// <param name="TimestampScale">Timestamp scale in nanoseconds (from Info).</param>
/// <param name="Duration">Segment duration from Info, if present.</param>
/// <param name="SegmentUuid">Segment UUID as lowercase hex, or null if absent or all-zero.</param>
/// <param name="SegmentFamily">Segment family UUID as lowercase hex, if present.</param>
/// <param name="SegmentFilename">Segment filename hint, if present.</param>
/// <param name="PrevUuid">Previous segment UUID as lowercase hex, if present.</param>
/// <param name="NextUuid">Next segment UUID as lowercase hex, if present.</param>
/// <param name="Editions">Edition entries from the Chapters section.</param>
public sealed record MatroskaSegmentInfo(
    long TimestampScale,
    double? Duration,
    string? SegmentUuid,
    string? SegmentFamily,
    string? SegmentFilename,
    string? PrevUuid,
    string? NextUuid,
    IReadOnlyList<MatroskaEdition> Editions);
