namespace Jellyfin.MediaEncoding.Keyframes.Matroska;

/// <summary>
/// Constants for the Matroska identifiers.
/// </summary>
public static class MatroskaConstants
{
    internal const ulong SegmentContainer = 0x18538067;

    internal const ulong SeekHead = 0x114D9B74;
    internal const ulong Seek = 0x4DBB;

    internal const ulong Info = 0x1549A966;
    internal const ulong TimestampScale = 0x2AD7B1;
    internal const ulong Duration = 0x4489;

    internal const ulong Tracks = 0x1654AE6B;
    internal const ulong TrackEntry = 0xAE;
    internal const ulong TrackNumber = 0xD7;
    internal const ulong TrackType = 0x83;

    internal const ulong TrackTypeVideo = 0x1;
    internal const ulong TrackTypeSubtitle = 0x11;

    internal const ulong Cues = 0x1C53BB6B;
    internal const ulong CueTime = 0xB3;
    internal const ulong CuePoint = 0xBB;
    internal const ulong CueTrackPositions = 0xB7;
    internal const ulong CuePointTrackNumber = 0xF7;

    // Info — segment identity
    internal const ulong SegmentUUID = 0x73A4;
    internal const ulong SegmentFilename = 0x7384;
    internal const ulong SegmentFamily = 0x4444;
    internal const ulong PrevUUID = 0x3CB923;
    internal const ulong NextUUID = 0x3EB923;

    // Chapters
    internal const ulong Chapters = 0x1043A770;
    internal const ulong EditionEntry = 0x45B9;
    internal const ulong EditionUID = 0x45BC;
    internal const ulong EditionFlagDefault = 0x45DB;
    internal const ulong EditionFlagOrdered = 0x45DD;
    internal const ulong EditionFlagHidden = 0x45BD;

    // ChapterAtom
    internal const ulong ChapterAtom = 0xB6;
    internal const ulong ChapterUID = 0x73C4;
    internal const ulong ChapterStringUID = 0x5654;
    internal const ulong ChapterTimeStart = 0x91;
    internal const ulong ChapterTimeEnd = 0x92;
    internal const ulong ChapterFlagHidden = 0x98;
    internal const ulong ChapterFlagEnabled = 0x4598;
    internal const ulong ChapterSegmentUID = 0x6E67;
    internal const ulong ChapterSegmentEditionUID = 0x6EBC;
    internal const ulong ChapterDisplay = 0x80;
    internal const ulong ChapString = 0x85;
    internal const ulong ChapLanguage = 0x437C;
}
