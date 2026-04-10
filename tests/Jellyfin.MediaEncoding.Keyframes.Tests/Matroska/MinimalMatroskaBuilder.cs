using System;
using System.IO;
using NEbml.Core;

namespace Jellyfin.MediaEncoding.Keyframes.Tests.Matroska;

/// <summary>
/// Builds minimal Matroska byte streams for chapter-parser tests (NEbml <see cref="EbmlWriter"/>).
/// </summary>
internal static class MinimalMatroskaBuilder
{
    // Element IDs as written on the wire (Matroska EBML encoding).
    private static readonly VInt IdSegment = ReadId(0x18, 0x53, 0x80, 0x67);
    private static readonly VInt IdSeekHead = ReadId(0x11, 0x4D, 0x9B, 0x74);
    private static readonly VInt IdSeek = ReadId(0x4D, 0xBB);
    private static readonly VInt IdInfo = ReadId(0x15, 0x49, 0xA9, 0x66);
    private static readonly VInt IdChapters = ReadId(0x10, 0x43, 0xA7, 0x70);
    private static readonly VInt IdTimestampScale = ReadId(0x2A, 0xD7, 0xB1);
    private static readonly VInt IdSegmentUuid = ReadId(0x73, 0xA4);
    private static readonly VInt IdEditionEntry = ReadId(0x45, 0xB9);
    private static readonly VInt IdEditionUid = ReadId(0x45, 0xBC);
    private static readonly VInt IdEditionFlagOrdered = ReadId(0x45, 0xDD);
    private static readonly VInt IdChapterAtom = ReadId(0xB6);
    private static readonly VInt IdChapterUid = ReadId(0x73, 0xC4);
    private static readonly VInt IdChapterTimeStart = ReadId(0x91);
    private static readonly VInt IdChapterTimeEnd = ReadId(0x92);
    private static readonly VInt IdChapterSegmentUid = ReadId(0x6E, 0x67);
    private static readonly VInt IdChapterSegmentEditionUid = ReadId(0x6E, 0xBC);
    private static readonly VInt IdEditionFlagDefault = ReadId(0x45, 0xDB);
    private static readonly VInt SeekIdElementId = ReadId(0x53, 0xAB);
    private static readonly VInt SeekPositionElementId = ReadId(0x53, 0xAC);

    // SeekID payloads (big-endian element IDs as in Matroska SeekID).
    private static readonly byte[] SeekIdInfo = { 0x15, 0x49, 0xA9, 0x66 };
    private static readonly byte[] SeekIdChapters = { 0x10, 0x43, 0xA7, 0x70 };

    public static byte[] Build(
        bool includeChaptersSeek,
        bool includeChaptersPayload,
        byte[]? segmentUuid,
        bool orderedEdition,
        bool chapterMissingEnd,
        byte[]? chapterSegmentUuid = null)
    {
        var infoElement = BuildInfoElement(segmentUuid);
        byte[]? chaptersElement = null;
        if (includeChaptersPayload)
        {
            chaptersElement = BuildChaptersElement(orderedEdition, chapterMissingEnd, chapterSegmentUuid);
        }

        // Converge SeekHead element size vs. SeekPosition values (positions are relative to Segment data start).
        long infoPos = 64;
        byte[] seekHeadElement = Array.Empty<byte>();
        for (var iter = 0; iter < 16; iter++)
        {
            var chapPos = includeChaptersSeek && chaptersElement != null
                ? infoPos + infoElement.Length
                : 0L;
            seekHeadElement = BuildSeekHeadElement(
                infoPos,
                includeChaptersSeek && chaptersElement != null ? chapPos : null);

            if (seekHeadElement.Length == infoPos)
            {
                break;
            }

            infoPos = seekHeadElement.Length;
        }

        using var ms = new MemoryStream();
        var w = new EbmlWriter(ms);
        using (var seg = w.StartMasterElement(IdSegment))
        {
            seg.Write(seekHeadElement, 0, seekHeadElement.Length);
            seg.Write(infoElement, 0, infoElement.Length);
            if (chaptersElement != null)
            {
                seg.Write(chaptersElement, 0, chaptersElement.Length);
            }
        }

        return ms.ToArray();
    }

    private static byte[] BuildSeekHeadElement(long infoPosition, long? chaptersPosition)
    {
        using var ms = new MemoryStream();
        var w = new EbmlWriter(ms);
        using (var sh = w.StartMasterElement(IdSeekHead))
        {
            WriteSeekEntry(sh, SeekIdInfo, (ulong)infoPosition);
            if (chaptersPosition.HasValue)
            {
                WriteSeekEntry(sh, SeekIdChapters, (ulong)chaptersPosition.Value);
            }
        }

        return ms.ToArray();
    }

    private static void WriteSeekEntry(EbmlWriter w, byte[] seekIdBigEndian, ulong position)
    {
        using var seek = w.StartMasterElement(IdSeek);
        seek.Write(SeekIdElementId, seekIdBigEndian, 0, seekIdBigEndian.Length);
        seek.Write(SeekPositionElementId, position);
    }

    private static byte[] BuildInfoElement(byte[]? segmentUuid)
    {
        using var ms = new MemoryStream();
        var w = new EbmlWriter(ms);
        using (var info = w.StartMasterElement(IdInfo))
        {
            info.Write(IdTimestampScale, 1_000_000UL);
            if (segmentUuid != null)
            {
                info.Write(IdSegmentUuid, segmentUuid, 0, segmentUuid.Length);
            }
        }

        return ms.ToArray();
    }

    private static byte[] BuildChaptersElement(bool orderedEdition, bool chapterMissingEnd, byte[]? chapterSegmentUuid)
    {
        using var ms = new MemoryStream();
        var w = new EbmlWriter(ms);
        using (var ch = w.StartMasterElement(IdChapters))
        {
            using (var ed = ch.StartMasterElement(IdEditionEntry))
            {
                ed.Write(IdEditionUid, 1UL);
                ed.Write(IdEditionFlagOrdered, orderedEdition ? 1UL : 0UL);
                using (var atom = ed.StartMasterElement(IdChapterAtom))
                {
                    atom.Write(IdChapterUid, 100UL);
                    atom.Write(IdChapterTimeStart, 0UL);
                    if (!chapterMissingEnd)
                    {
                        atom.Write(IdChapterTimeEnd, 1_000_000UL);
                    }

                    if (chapterSegmentUuid != null)
                    {
                        atom.Write(IdChapterSegmentUid, chapterSegmentUuid, 0, chapterSegmentUuid.Length);
                    }
                }
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Two sequential local chapter atoms in one ordered edition (times in nanoseconds as stored in Matroska).
    /// </summary>
    public static byte[] BuildTwoLocalOrderedChapters(
        byte[]? segmentUuid,
        ulong editionUid,
        ulong chapter1StartNs,
        ulong chapter1EndNs,
        ulong chapter2StartNs,
        ulong chapter2EndNs)
    {
        var infoElement = BuildInfoElement(segmentUuid);
        var chaptersElement = BuildChaptersElementTwoLocal(
            editionUid,
            chapter1StartNs,
            chapter1EndNs,
            chapter2StartNs,
            chapter2EndNs);

        long infoPos = 64;
        byte[] seekHeadElement = Array.Empty<byte>();
        for (var iter = 0; iter < 16; iter++)
        {
            var chapPos = infoPos + infoElement.Length;
            seekHeadElement = BuildSeekHeadElement(infoPos, chapPos);
            if (seekHeadElement.Length == infoPos)
            {
                break;
            }

            infoPos = seekHeadElement.Length;
        }

        using var ms = new MemoryStream();
        var w = new EbmlWriter(ms);
        using (var seg = w.StartMasterElement(IdSegment))
        {
            seg.Write(seekHeadElement, 0, seekHeadElement.Length);
            seg.Write(infoElement, 0, infoElement.Length);
            seg.Write(chaptersElement, 0, chaptersElement.Length);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// One local chapter in an ordered edition (custom edition UID for linked-edition targets).
    /// </summary>
    public static byte[] BuildSingleLocalOrderedChapter(
        byte[]? segmentUuid,
        ulong editionUid,
        ulong timeStartNs,
        ulong timeEndNs)
    {
        var infoElement = BuildInfoElement(segmentUuid);
        var chaptersElement = BuildChaptersElementOneLocal(editionUid, timeStartNs, timeEndNs);

        long infoPos = 64;
        byte[] seekHeadElement = Array.Empty<byte>();
        for (var iter = 0; iter < 16; iter++)
        {
            var chapPos = infoPos + infoElement.Length;
            seekHeadElement = BuildSeekHeadElement(infoPos, chapPos);
            if (seekHeadElement.Length == infoPos)
            {
                break;
            }

            infoPos = seekHeadElement.Length;
        }

        using var ms = new MemoryStream();
        var w = new EbmlWriter(ms);
        using (var seg = w.StartMasterElement(IdSegment))
        {
            seg.Write(seekHeadElement, 0, seekHeadElement.Length);
            seg.Write(infoElement, 0, infoElement.Length);
            seg.Write(chaptersElement, 0, chaptersElement.Length);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// One ordered chapter that links to another segment and edition (cross-item ordered-chapter tests).
    /// </summary>
    public static byte[] BuildLinkedOrderedChapter(
        byte[]? segmentUuid,
        ulong editionUid,
        byte[] targetSegmentUid16,
        ulong targetEditionUid,
        ulong timeStartNs,
        ulong timeEndNs)
    {
        var infoElement = BuildInfoElement(segmentUuid);
        var chaptersElement = BuildChaptersElementLinked(
            editionUid,
            targetSegmentUid16,
            targetEditionUid,
            timeStartNs,
            timeEndNs);

        long infoPos = 64;
        byte[] seekHeadElement = Array.Empty<byte>();
        for (var iter = 0; iter < 16; iter++)
        {
            var chapPos = infoPos + infoElement.Length;
            seekHeadElement = BuildSeekHeadElement(infoPos, chapPos);
            if (seekHeadElement.Length == infoPos)
            {
                break;
            }

            infoPos = seekHeadElement.Length;
        }

        using var ms = new MemoryStream();
        var w = new EbmlWriter(ms);
        using (var seg = w.StartMasterElement(IdSegment))
        {
            seg.Write(seekHeadElement, 0, seekHeadElement.Length);
            seg.Write(infoElement, 0, infoElement.Length);
            seg.Write(chaptersElement, 0, chaptersElement.Length);
        }

        return ms.ToArray();
    }

    private static byte[] BuildChaptersElementOneLocal(ulong editionUid, ulong timeStartNs, ulong timeEndNs)
    {
        using var ms = new MemoryStream();
        var w = new EbmlWriter(ms);
        using (var ch = w.StartMasterElement(IdChapters))
        {
            using (var ed = ch.StartMasterElement(IdEditionEntry))
            {
                ed.Write(IdEditionUid, editionUid);
                ed.Write(IdEditionFlagOrdered, 1UL);
                ed.Write(IdEditionFlagDefault, 1UL);
                using (var atom = ed.StartMasterElement(IdChapterAtom))
                {
                    atom.Write(IdChapterUid, 300UL);
                    atom.Write(IdChapterTimeStart, timeStartNs);
                    atom.Write(IdChapterTimeEnd, timeEndNs);
                }
            }
        }

        return ms.ToArray();
    }

    private static byte[] BuildChaptersElementTwoLocal(
        ulong editionUid,
        ulong chapter1StartNs,
        ulong chapter1EndNs,
        ulong chapter2StartNs,
        ulong chapter2EndNs)
    {
        using var ms = new MemoryStream();
        var w = new EbmlWriter(ms);
        using (var ch = w.StartMasterElement(IdChapters))
        {
            using (var ed = ch.StartMasterElement(IdEditionEntry))
            {
                ed.Write(IdEditionUid, editionUid);
                ed.Write(IdEditionFlagOrdered, 1UL);
                ed.Write(IdEditionFlagDefault, 1UL);
                using (var atom = ed.StartMasterElement(IdChapterAtom))
                {
                    atom.Write(IdChapterUid, 100UL);
                    atom.Write(IdChapterTimeStart, chapter1StartNs);
                    atom.Write(IdChapterTimeEnd, chapter1EndNs);
                }

                using (var atom = ed.StartMasterElement(IdChapterAtom))
                {
                    atom.Write(IdChapterUid, 101UL);
                    atom.Write(IdChapterTimeStart, chapter2StartNs);
                    atom.Write(IdChapterTimeEnd, chapter2EndNs);
                }
            }
        }

        return ms.ToArray();
    }

    private static byte[] BuildChaptersElementLinked(
        ulong editionUid,
        byte[] targetSegmentUid16,
        ulong targetEditionUid,
        ulong timeStartNs,
        ulong timeEndNs)
    {
        using var ms = new MemoryStream();
        var w = new EbmlWriter(ms);
        using (var ch = w.StartMasterElement(IdChapters))
        {
            using (var ed = ch.StartMasterElement(IdEditionEntry))
            {
                ed.Write(IdEditionUid, editionUid);
                ed.Write(IdEditionFlagOrdered, 1UL);
                ed.Write(IdEditionFlagDefault, 1UL);
                using (var atom = ed.StartMasterElement(IdChapterAtom))
                {
                    atom.Write(IdChapterUid, 200UL);
                    atom.Write(IdChapterTimeStart, timeStartNs);
                    atom.Write(IdChapterTimeEnd, timeEndNs);
                    atom.Write(IdChapterSegmentUid, targetSegmentUid16, 0, targetSegmentUid16.Length);
                    atom.Write(IdChapterSegmentEditionUid, targetEditionUid);
                }
            }
        }

        return ms.ToArray();
    }

    private static VInt ReadId(params byte[] wire)
    {
        var ms = new MemoryStream(wire);
        return VInt.Read(ms, 8, new byte[8]);
    }
}
