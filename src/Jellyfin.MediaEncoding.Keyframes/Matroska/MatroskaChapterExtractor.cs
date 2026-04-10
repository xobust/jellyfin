using System;
using System.IO;
using Jellyfin.MediaEncoding.Keyframes.Matroska.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NEbml.Core;

namespace Jellyfin.MediaEncoding.Keyframes.Matroska;

/// <summary>
/// Extracts Matroska segment identity and chapter metadata from an MKV/MKS file.
/// </summary>
public static class MatroskaChapterExtractor
{
    /// <summary>
    /// Reads segment Info and Chapters from the file. Does not modify the keyframe extraction path.
    /// </summary>
    /// <param name="filePath">Path to the Matroska file.</param>
    /// <param name="logger">Optional logger for parse warnings.</param>
    /// <returns>Segment identity and edition/chapter data.</returns>
    public static MatroskaSegmentInfo GetSegmentInfo(string filePath, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        using var stream = File.OpenRead(filePath);
        using var reader = new EbmlReader(stream);
        var seek = MatroskaChapterReader.ReadSeekHead(reader);
        var info = MatroskaChapterReader.ReadSegmentInfo(reader, seek.InfoPosition, logger);
        var editions = MatroskaChapterReader.ReadChaptersElement(reader, seek.ChaptersPosition, logger);
        return info with { Editions = editions };
    }
}
