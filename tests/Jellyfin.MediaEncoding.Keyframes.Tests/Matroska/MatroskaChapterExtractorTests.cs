using System;
using System.IO;
using Jellyfin.MediaEncoding.Keyframes.Matroska;
using Jellyfin.MediaEncoding.Keyframes.Matroska.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NEbml.Core;
using Xunit;

namespace Jellyfin.MediaEncoding.Keyframes.Tests.Matroska;

/// <summary>
/// Tests for <see cref="MatroskaChapterExtractor"/> using minimal hand-built EBML buffers.
/// </summary>
public class MatroskaChapterExtractorTests
{
    [Fact]
    public void GetSegmentInfo_NoChaptersSeek_ReturnsEmptyEditions()
    {
        var bytes = MinimalMatroskaBuilder.Build(
            includeChaptersSeek: false,
            includeChaptersPayload: false,
            segmentUuid: new byte[]
            {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10
            },
            orderedEdition: false,
            chapterMissingEnd: false);

        var path = WriteTemp(bytes);
        try
        {
            var info = MatroskaChapterExtractor.GetSegmentInfo(path, NullLogger.Instance);
            Assert.Equal(1_000_000L, info.TimestampScale);
            Assert.Equal("0102030405060708090a0b0c0d0e0f10", info.SegmentUuid);
            Assert.Empty(info.Editions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetSegmentInfo_OrderedEditionWithMissingEnd_IsNotOrdered()
    {
        var bytes = MinimalMatroskaBuilder.Build(
            includeChaptersSeek: true,
            includeChaptersPayload: true,
            segmentUuid: new byte[16],
            orderedEdition: true,
            chapterMissingEnd: true);

        var path = WriteTemp(bytes);
        try
        {
            var info = MatroskaChapterExtractor.GetSegmentInfo(path, NullLogger.Instance);
            Assert.Single(info.Editions);
            Assert.False(info.Editions[0].IsOrdered);
            Assert.Single(info.Editions[0].Chapters);
            Assert.Null(info.Editions[0].Chapters[0].TimeEndTicks);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetSegmentInfo_OrderedWithSegmentLink_ParsesChapterSegmentUuid()
    {
        var linkTarget = new byte[]
        {
            0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff, 0x11, 0x22,
            0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0x00
        };

        var bytes = MinimalMatroskaBuilder.Build(
            includeChaptersSeek: true,
            includeChaptersPayload: true,
            segmentUuid: new byte[]
            {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10
            },
            orderedEdition: true,
            chapterMissingEnd: false,
            chapterSegmentUuid: linkTarget);

        var path = WriteTemp(bytes);
        try
        {
            var info = MatroskaChapterExtractor.GetSegmentInfo(path, NullLogger.Instance);
            Assert.Single(info.Editions);
            Assert.True(info.Editions[0].IsOrdered);
            Assert.Single(info.Editions[0].Chapters);
            Assert.Equal("aabbccddeeff11223344556677889900", info.Editions[0].Chapters[0].SegmentUuid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetSegmentInfo_AllZeroSegmentUuid_YieldsNull()
    {
        var bytes = MinimalMatroskaBuilder.Build(
            includeChaptersSeek: false,
            includeChaptersPayload: false,
            segmentUuid: new byte[16],
            orderedEdition: false,
            chapterMissingEnd: false);

        var path = WriteTemp(bytes);
        try
        {
            var info = MatroskaChapterExtractor.GetSegmentInfo(path, NullLogger.Instance);
            Assert.Null(info.SegmentUuid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadSeekHead_ChapterOnly_NoTracksOrCues_DoesNotThrow()
    {
        var bytes = MinimalMatroskaBuilder.Build(
            includeChaptersSeek: true,
            includeChaptersPayload: true,
            segmentUuid: null,
            orderedEdition: false,
            chapterMissingEnd: false);

        using var stream = new MemoryStream(bytes);
        using var reader = new EbmlReader(stream);
        var seek = MatroskaChapterReader.ReadSeekHead(reader);
        Assert.Null(seek.TracksPosition);
        Assert.Null(seek.CuesPosition);
        Assert.NotNull(seek.ChaptersPosition);
    }

    private static string WriteTemp(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "matroska-test-" + Guid.NewGuid().ToString("N") + ".mkv");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
