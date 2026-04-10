namespace Jellyfin.MediaEncoding.Keyframes.Matroska.Models;

/// <summary>
/// Seek positions for Matroska chapter parsing. All positions are relative to the Segment container.
/// Tracks and Cues may be absent (e.g. chapter-only .mks); Chapters may be absent.
/// </summary>
internal sealed record ChapterSeekHead(
    long InfoPosition,
    long? TracksPosition,
    long? CuesPosition,
    long? ChaptersPosition);
