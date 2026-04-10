using System.Collections.Generic;

namespace Jellyfin.MediaEncoding.Keyframes.Matroska.Models;

/// <summary>
/// A Matroska edition entry with its chapter atoms.
/// </summary>
public sealed record MatroskaEdition(
    ulong EditionUid,
    bool IsOrdered,
    bool IsDefault,
    bool IsHidden,
    IReadOnlyList<MatroskaChapter> Chapters);
