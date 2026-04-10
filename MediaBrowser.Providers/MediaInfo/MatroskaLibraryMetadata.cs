using System;
using System.IO;
using System.Linq;
using Jellyfin.MediaEncoding.Keyframes.Matroska;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Providers.MediaInfo;

/// <summary>
/// Populates Matroska segment identity and ordered-chapter flags on library items after ffprobe.
/// </summary>
public static class MatroskaLibraryMetadata
{
    /// <summary>
    /// Sets <see cref="BaseItem.SegmentUuid"/> and <see cref="BaseItem.HasOrderedChapters"/> from the file when it is a local Matroska container; otherwise clears both fields.
    /// </summary>
    /// <param name="item">The library item to update.</param>
    /// <param name="pathForLocalFile">Resolved path used for probing (shortcut targets included).</param>
    /// <param name="protocol">Protocol for <paramref name="pathForLocalFile"/>.</param>
    /// <param name="logger">Logger for parse failures.</param>
    public static void Populate(BaseItem item, string pathForLocalFile, MediaProtocol protocol, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(logger);

        if (protocol != MediaProtocol.File || string.IsNullOrEmpty(pathForLocalFile))
        {
            Clear(item);
            return;
        }

        if (!IsMatroskaExtension(pathForLocalFile))
        {
            Clear(item);
            return;
        }

        try
        {
            var info = MatroskaChapterExtractor.GetSegmentInfo(pathForLocalFile, logger);
            item.SegmentUuid = info.SegmentUuid;
            item.HasOrderedChapters = info.Editions.Any(e => e.IsOrdered);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read Matroska segment metadata for {Path}", pathForLocalFile);
            Clear(item);
        }
    }

    private static void Clear(BaseItem item)
    {
        item.SegmentUuid = null;
        item.HasOrderedChapters = false;
    }

    private static bool IsMatroskaExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".mka", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".mks", StringComparison.OrdinalIgnoreCase);
    }
}
