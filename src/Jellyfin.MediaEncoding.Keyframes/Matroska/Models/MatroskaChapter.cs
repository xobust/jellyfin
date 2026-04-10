namespace Jellyfin.MediaEncoding.Keyframes.Matroska.Models;

/// <summary>
/// A single chapter atom (leaf) from Matroska Chapters.
/// </summary>
/// <param name="ChapterUid">Chapter UID.</param>
/// <param name="ChapterStringUid">Optional string UID.</param>
/// <param name="TimeStartTicks">Chapter start in 100 ns ticks (raw chapter time / 100; not scaled by TimestampScale).</param>
/// <param name="TimeEndTicks">Chapter end in 100 ns ticks, or null if missing.</param>
/// <param name="SegmentUuid">Linked segment UUID (lowercase hex), or null for local chapters.</param>
/// <param name="SegmentEditionUid">Optional edition UID for linked chapters.</param>
/// <param name="Name">Display name from ChapterDisplay.</param>
public sealed record MatroskaChapter(
    ulong ChapterUid,
    string? ChapterStringUid,
    long TimeStartTicks,
    long? TimeEndTicks,
    string? SegmentUuid,
    ulong? SegmentEditionUid,
    string Name);
