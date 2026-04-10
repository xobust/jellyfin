using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.MediaEncoding.Keyframes.Matroska.Extensions;
using Jellyfin.MediaEncoding.Keyframes.Matroska.Models;
using Microsoft.Extensions.Logging;
using NEbml.Core;

namespace Jellyfin.MediaEncoding.Keyframes.Matroska;

/// <summary>
/// Reads Matroska segment identity and chapter metadata for ordered-chapter support.
/// </summary>
/// <remarks>
/// Matroska naming: the <c>Chapters</c> element is a <strong>container</strong> for one or more <c>EditionEntry</c>
/// (editions). Each edition contains a tree of <c>ChapterAtom</c> elements; leaf atoms become <see cref="MatroskaChapter"/>.
/// </remarks>
internal static class MatroskaChapterReader
{
    /// <summary>
    /// Reads SeekHead for Info (required), optional Tracks, Cues, and Chapters positions.
    /// </summary>
    /// <param name="reader">The EBML reader positioned at file offset 0.</param>
    /// <returns>Resolved seek positions relative to the Segment container.</returns>
    internal static ChapterSeekHead ReadSeekHead(EbmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.ElementPosition != 0)
        {
            throw new InvalidOperationException("File position must be at 0");
        }

        if (!reader.FindElement(MatroskaConstants.SegmentContainer))
        {
            throw new InvalidOperationException("Expected a segment container");
        }

        reader.EnterContainer();

        long? tracksPosition = null;
        long? cuesPosition = null;
        long? infoPosition = null;
        long? chaptersPosition = null;

        if (!reader.FindElement(MatroskaConstants.SeekHead))
        {
            throw new InvalidOperationException("Expected a SeekHead");
        }

        reader.EnterContainer();
        while (reader.FindElement(MatroskaConstants.Seek))
        {
            reader.EnterContainer();
            reader.ReadNext();
            var type = (ulong)reader.ReadUIntFromBinary();
            switch (type)
            {
                case MatroskaConstants.Tracks:
                    reader.ReadNext();
                    tracksPosition = (long)reader.ReadUInt();
                    break;
                case MatroskaConstants.Cues:
                    reader.ReadNext();
                    cuesPosition = (long)reader.ReadUInt();
                    break;
                case MatroskaConstants.Info:
                    reader.ReadNext();
                    infoPosition = (long)reader.ReadUInt();
                    break;
                case MatroskaConstants.Chapters:
                    reader.ReadNext();
                    chaptersPosition = (long)reader.ReadUInt();
                    break;
            }

            reader.LeaveContainer();
        }

        reader.LeaveContainer();

        if (!infoPosition.HasValue)
        {
            throw new InvalidOperationException("SeekHead does not contain Info position");
        }

        return new ChapterSeekHead(infoPosition.Value, tracksPosition, cuesPosition, chaptersPosition);
    }

    /// <summary>
    /// Reads the Info element at <paramref name="infoPosition"/> (relative to Segment).
    /// </summary>
    /// <param name="reader">The EBML reader, still positioned inside the Segment container.</param>
    /// <param name="infoPosition">The Info element position relative to the Segment container.</param>
    /// <param name="logger">Logger for parse warnings.</param>
    /// <returns>Segment identity with an empty <c>Editions</c> list.</returns>
    internal static MatroskaSegmentInfo ReadSegmentInfo(EbmlReader reader, long infoPosition, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(logger);

        reader.ReadAt(infoPosition);
        reader.EnterContainer();

        long timestampScale = 0;
        double? duration = null;
        byte[]? segmentUuidBin = null;
        byte[]? segmentFamilyBin = null;
        string? segmentFilename = null;
        byte[]? prevUuidBin = null;
        byte[]? nextUuidBin = null;

        while (reader.ReadNext())
        {
            switch (reader.ElementId.EncodedValue)
            {
                case MatroskaConstants.TimestampScale:
                    timestampScale = (long)reader.ReadUInt();
                    break;
                case MatroskaConstants.Duration:
                    duration = reader.ReadFloat();
                    break;
                case MatroskaConstants.SegmentUUID:
                    segmentUuidBin = ReadFullBinaryElement(reader);
                    break;
                case MatroskaConstants.SegmentFamily:
                    segmentFamilyBin = ReadFullBinaryElement(reader);
                    break;
                case MatroskaConstants.SegmentFilename:
                    segmentFilename = reader.ReadUtf();
                    break;
                case MatroskaConstants.PrevUUID:
                    prevUuidBin = ReadFullBinaryElement(reader);
                    break;
                case MatroskaConstants.NextUUID:
                    nextUuidBin = ReadFullBinaryElement(reader);
                    break;
            }
        }

        reader.LeaveContainer();

        if (timestampScale == 0)
        {
            throw new InvalidOperationException("Info is missing mandatory TimestampScale");
        }

        return new MatroskaSegmentInfo(
            timestampScale,
            duration,
            BinaryUuidToHex(segmentUuidBin, "SegmentUUID", logger),
            BinaryUuidToHex(segmentFamilyBin, "SegmentFamily", logger),
            segmentFilename,
            BinaryUuidToHex(prevUuidBin, "PrevUUID", logger),
            BinaryUuidToHex(nextUuidBin, "NextUUID", logger),
            Array.Empty<MatroskaEdition>());
    }

    /// <summary>
    /// Reads the Matroska <c>Chapters</c> master element (not a single chapter): walks <c>EditionEntry</c> children
    /// and collects leaf <c>ChapterAtom</c> rows per edition as <see cref="MatroskaEdition"/> / <see cref="MatroskaChapter"/>.
    /// </summary>
    /// <param name="reader">The EBML reader, still positioned inside the Segment container.</param>
    /// <param name="chaptersPosition">The <c>Chapters</c> element position relative to the Segment, or null if absent.</param>
    /// <param name="logger">Logger for parse warnings.</param>
    /// <returns>One <see cref="MatroskaEdition"/> per <c>EditionEntry</c>, or an empty list.</returns>
    internal static IReadOnlyList<MatroskaEdition> ReadChaptersElement(EbmlReader reader, long? chaptersPosition, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(logger);

        if (!chaptersPosition.HasValue)
        {
            return Array.Empty<MatroskaEdition>();
        }

        reader.ReadAt(chaptersPosition.Value);
        reader.EnterContainer();

        var editions = new List<MatroskaEdition>();
        while (reader.FindElement(MatroskaConstants.EditionEntry))
        {
            editions.Add(ReadEditionEntry(reader, logger));
        }

        reader.LeaveContainer();
        return editions;
    }

    /// <summary>
    /// Parses one <c>EditionEntry</c> (one edition: flags + chapter atoms).
    /// </summary>
    private static MatroskaEdition ReadEditionEntry(EbmlReader reader, ILogger logger)
    {
        reader.EnterContainer();

        ulong editionUid = 0;
        bool isOrdered = false;
        bool isDefault = false;
        bool isHidden = false;
        var chapters = new List<MatroskaChapter>();

        while (reader.ReadNext())
        {
            if (reader.ElementId.EncodedValue == MatroskaConstants.ChapterAtom)
            {
                ParseChapterAtomSubtree(reader, logger, chapters);
                continue;
            }

            switch (reader.ElementId.EncodedValue)
            {
                case MatroskaConstants.EditionUID:
                    editionUid = reader.ReadUInt();
                    break;
                case MatroskaConstants.EditionFlagOrdered:
                    isOrdered = reader.ReadUInt() != 0;
                    break;
                case MatroskaConstants.EditionFlagDefault:
                    isDefault = reader.ReadUInt() != 0;
                    break;
                case MatroskaConstants.EditionFlagHidden:
                    isHidden = reader.ReadUInt() != 0;
                    break;
            }
        }

        reader.LeaveContainer();

        if (isOrdered)
        {
            foreach (var ch in chapters)
            {
                if (!ch.TimeEndTicks.HasValue)
                {
                    logger.LogWarning("Ordered edition {EditionUid} has a chapter without ChapterTimeEnd; treating edition as non-ordered", editionUid);
                    isOrdered = false;
                    break;
                }
            }
        }

        return new MatroskaEdition(editionUid, isOrdered, isDefault, isHidden, chapters);
    }

    /// <summary>
    /// Parses one <c>ChapterAtom</c> subtree: nested <c>ChapterAtom</c> children recurse; a leaf with
    /// <c>ChapterTimeStart</c> (and no nested atoms) becomes one <see cref="MatroskaChapter"/>.
    /// </summary>
    private static void ParseChapterAtomSubtree(EbmlReader reader, ILogger logger, List<MatroskaChapter> output)
    {
        reader.EnterContainer();

        ulong? chapterUid = null;
        string? chapterStringUid = null;
        ulong? timeStartNs = null;
        ulong? timeEndNs = null;
        string? segmentUuid = null;
        ulong? segmentEditionUid = null;
        string name = string.Empty;
        var hasNestedChapterAtom = false;

        while (reader.ReadNext())
        {
            var id = reader.ElementId.EncodedValue;
            if (id == MatroskaConstants.ChapterAtom)
            {
                hasNestedChapterAtom = true;
                ParseChapterAtomSubtree(reader, logger, output);
                continue;
            }

            switch (id)
            {
                case MatroskaConstants.ChapterUID:
                    chapterUid = reader.ReadUInt();
                    break;
                case MatroskaConstants.ChapterStringUID:
                    chapterStringUid = reader.ReadUtf();
                    break;
                case MatroskaConstants.ChapterTimeStart:
                    timeStartNs = reader.ReadUInt();
                    break;
                case MatroskaConstants.ChapterTimeEnd:
                    timeEndNs = reader.ReadUInt();
                    break;
                case MatroskaConstants.ChapterSegmentUID:
                    segmentUuid = BinaryUuidToHex(ReadFullBinaryElement(reader), "ChapterSegmentUID", logger);
                    break;
                case MatroskaConstants.ChapterSegmentEditionUID:
                    segmentEditionUid = reader.ReadUInt();
                    break;
                case MatroskaConstants.ChapterDisplay:
                    name = ReadChapterDisplayName(reader);
                    break;
            }
        }

        reader.LeaveContainer();

        if (hasNestedChapterAtom || !timeStartNs.HasValue)
        {
            return;
        }

        output.Add(new MatroskaChapter(
            chapterUid ?? 0,
            chapterStringUid,
            (long)(timeStartNs.Value / 100UL),
            timeEndNs.HasValue ? (long)(timeEndNs.Value / 100UL) : null,
            segmentUuid,
            segmentEditionUid,
            string.IsNullOrEmpty(name) ? string.Empty : name));
    }

    private static string ReadChapterDisplayName(EbmlReader reader)
    {
        reader.EnterContainer();
        var name = string.Empty;
        while (reader.ReadNext())
        {
            if (reader.ElementId.EncodedValue == MatroskaConstants.ChapString)
            {
                name = reader.ReadUtf();
                break;
            }
        }

        reader.LeaveContainer();
        return name;
    }

    private static byte[] ReadFullBinaryElement(EbmlReader reader)
    {
        var size = (int)reader.ElementSize;
        if (size < 0)
        {
            throw new EbmlDataFormatException("Invalid binary element size");
        }

        var buf = new byte[size];
        var offset = 0;
        while (offset < size)
        {
            var r = reader.ReadBinary(buf, offset, size - offset);
            if (r <= 0)
            {
                throw new EndOfStreamException();
            }

            offset += r;
        }

        return buf;
    }

    private static string? BinaryUuidToHex(byte[]? bytes, string fieldName, ILogger logger)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return null;
        }

        var allZero = true;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0)
            {
                allZero = false;
                break;
            }
        }

        if (allZero)
        {
            logger.LogWarning("{FieldName} is all-zero; treating as absent", fieldName);
            return null;
        }

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
