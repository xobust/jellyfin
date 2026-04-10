using System;
using System.IO;
using Jellyfin.MediaEncoding.Keyframes.Tests.Matroska;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Providers.MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Providers.Tests.MediaInfo;

public class MatroskaLibraryMetadataTests
{
    [Fact]
    public void Populate_OrderedMkv_SetsSegmentUuidAndHasOrderedChapters()
    {
        var bytes = MinimalMatroskaBuilder.Build(
            includeChaptersSeek: true,
            includeChaptersPayload: true,
            segmentUuid: new byte[]
            {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10
            },
            orderedEdition: true,
            chapterMissingEnd: false);

        var path = WriteTemp(bytes);
        try
        {
            var movie = new Movie();
            MatroskaLibraryMetadata.Populate(movie, path, MediaProtocol.File, NullLogger.Instance);
            Assert.Equal("0102030405060708090a0b0c0d0e0f10", movie.SegmentUuid);
            Assert.True(movie.HasOrderedChapters);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Populate_NonMatroskaExtension_ClearsFields()
    {
        var movie = new Movie
        {
            SegmentUuid = "deadbeef",
            HasOrderedChapters = true
        };

        MatroskaLibraryMetadata.Populate(movie, "/tmp/foo.mp4", MediaProtocol.File, NullLogger.Instance);

        Assert.Null(movie.SegmentUuid);
        Assert.False(movie.HasOrderedChapters);
    }

    [Fact]
    public void Populate_NonFileProtocol_ClearsFields()
    {
        var movie = new Movie
        {
            SegmentUuid = "deadbeef",
            HasOrderedChapters = true
        };

        MatroskaLibraryMetadata.Populate(movie, "http://example.com/a.mkv", MediaProtocol.Http, NullLogger.Instance);

        Assert.Null(movie.SegmentUuid);
        Assert.False(movie.HasOrderedChapters);
    }

    [Fact]
    public void Populate_RescanDifferentFixture_UpdatesFields()
    {
        var bytesOrdered = MinimalMatroskaBuilder.Build(
            includeChaptersSeek: true,
            includeChaptersPayload: true,
            segmentUuid: new byte[]
            {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10
            },
            orderedEdition: true,
            chapterMissingEnd: false);

        var bytesNoUuid = MinimalMatroskaBuilder.Build(
            includeChaptersSeek: false,
            includeChaptersPayload: false,
            segmentUuid: new byte[16],
            orderedEdition: false,
            chapterMissingEnd: false);

        var path1 = WriteTemp(bytesOrdered);
        var path2 = WriteTemp(bytesNoUuid);
        try
        {
            var movie = new Movie();
            MatroskaLibraryMetadata.Populate(movie, path1, MediaProtocol.File, NullLogger.Instance);
            Assert.Equal("0102030405060708090a0b0c0d0e0f10", movie.SegmentUuid);
            Assert.True(movie.HasOrderedChapters);

            MatroskaLibraryMetadata.Populate(movie, path2, MediaProtocol.File, NullLogger.Instance);
            Assert.Null(movie.SegmentUuid);
            Assert.False(movie.HasOrderedChapters);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    private static string WriteTemp(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "matroska-libmeta-test-" + Guid.NewGuid().ToString("N") + ".mkv");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
