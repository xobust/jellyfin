using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.MediaEncoding.Hls.OrderedChapters;
using Jellyfin.MediaEncoding.Keyframes.Tests.Matroska;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Streaming;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.MediaEncoding.Hls.Tests.OrderedChapters;

public class OrderedChapterTimelineBuilderTests
{
    private static string WriteTemp(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "octb-" + Guid.NewGuid().ToString("N") + ".mkv");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    [Fact]
    public void SameFile_TwoLocalChapters_ProducesTwoParts()
    {
        var seg = new byte[16];
        seg[0] = 0x01;
        // Gap between chapters > 100ms so merge does not collapse to one part.
        var bytes = MinimalMatroskaBuilder.BuildTwoLocalOrderedChapters(
            seg,
            editionUid: 1,
            chapter1StartNs: 0,
            chapter1EndNs: 500_000_000,
            chapter2StartNs: 650_000_000,
            chapter2EndNs: 1_150_000_000);

        var path = WriteTemp(bytes);
        try
        {
            var id = Guid.NewGuid();
            var mock = new Mock<IItemRepository>();
            var sut = new OrderedChapterTimelineBuilder(mock.Object);
            var result = sut.Build(id, path, NullLogger.Instance);

            Assert.Equal(2, result.Parts.Count);
            Assert.All(result.Parts, p => Assert.Equal(id, p.ItemId));
            Assert.False(result.Parts[0].IsMissing);
            Assert.Equal(0, result.MissingTicks);
            Assert.Equal(id, result.LayoutItemId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Merge_AdjacentSameSourceWithin100ms_MergesToOnePart()
    {
        var seg = new byte[16];
        seg[0] = 0x02;
        // Chapter times in ticks: end1 = 10_000_000 (1s); start2 = 10_050_000 (gap 50ms)
        const long t1EndTicks = 10_000_000L;
        const long t2StartTicks = 10_050_000L;
        var c1eNs = (ulong)(t1EndTicks * 100L);
        var c2sNs = (ulong)(t2StartTicks * 100L);
        var c2eNs = c2sNs + 500_000_000UL;

        var bytes = MinimalMatroskaBuilder.BuildTwoLocalOrderedChapters(
            seg,
            editionUid: 1,
            chapter1StartNs: 0,
            chapter1EndNs: c1eNs,
            chapter2StartNs: c2sNs,
            chapter2EndNs: c2eNs);

        var path = WriteTemp(bytes);
        try
        {
            var id = Guid.NewGuid();
            var mock = new Mock<IItemRepository>();
            var sut = new OrderedChapterTimelineBuilder(mock.Object);
            var result = sut.Build(id, path, NullLogger.Instance);

            Assert.Single(result.Parts);
            Assert.Equal(0L, result.Parts[0].SourceStartTicks);
            Assert.Equal((long)(c2eNs / 100UL), result.Parts[0].SourceEndTicks);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnresolvedLink_AddsMissingTicks()
    {
        var seg = new byte[16];
        seg[0] = 0x03;
        var targetUid = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            targetUid[i] = (byte)(0x80 + i);
        }

        var bytes = MinimalMatroskaBuilder.Build(
            includeChaptersSeek: true,
            includeChaptersPayload: true,
            segmentUuid: seg,
            orderedEdition: true,
            chapterMissingEnd: false,
            chapterSegmentUuid: targetUid);

        var path = WriteTemp(bytes);
        try
        {
            var id = Guid.NewGuid();
            var mock = new Mock<IItemRepository>();
            mock.Setup(r => r.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new List<BaseItem>());
            var sut = new OrderedChapterTimelineBuilder(mock.Object);
            var result = sut.Build(id, path, NullLogger.Instance);

            Assert.Single(result.Parts);
            Assert.True(result.Parts[0].IsMissing);
            Assert.True(result.MissingTicks > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LinkedDuration_UsesTargetPath()
    {
        var mainSeg = new byte[16];
        mainSeg[0] = 0x04;
        var targetSeg = new byte[16];
        targetSeg[0] = 0x40;

        var mainBytes = MinimalMatroskaBuilder.Build(
            includeChaptersSeek: true,
            includeChaptersPayload: true,
            segmentUuid: mainSeg,
            orderedEdition: true,
            chapterMissingEnd: false,
            chapterSegmentUuid: targetSeg);

        var targetBytes = MinimalMatroskaBuilder.BuildSingleLocalOrderedChapter(
            targetSeg,
            editionUid: 1,
            timeStartNs: 0,
            timeEndNs: 5_000_000_000UL);

        var mainPath = WriteTemp(mainBytes);
        var targetPath = WriteTemp(targetBytes);
        try
        {
            var mainId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var targetItem = new Movie { Id = targetId, Path = targetPath };
            var mock = new Mock<IItemRepository>();
            mock.Setup(r => r.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery q) =>
                    q.SegmentUuid == Hex(targetSeg)
                        ? new List<BaseItem> { targetItem }
                        : new List<BaseItem>());

            var sut = new OrderedChapterTimelineBuilder(mock.Object);
            var result = sut.Build(mainId, mainPath, NullLogger.Instance);

            Assert.Single(result.Parts);
            Assert.Equal(targetId, result.Parts[0].ItemId);
            Assert.Equal(targetPath, result.Parts[0].Path);
            Assert.Equal(targetId, result.LayoutItemId);
        }
        finally
        {
            File.Delete(mainPath);
            File.Delete(targetPath);
        }
    }

    [Fact]
    public void LinkedEdition_ExpandsTargetEdition()
    {
        var segMain = new byte[16];
        segMain[0] = 0x05;
        var segTarget = new byte[16];
        segTarget[0] = 0x55;
        const ulong targetEditionUid = 7UL;

        var mainBytes = MinimalMatroskaBuilder.BuildLinkedOrderedChapter(
            segMain,
            editionUid: 1,
            targetSegmentUid16: segTarget,
            targetEditionUid: targetEditionUid,
            timeStartNs: 0,
            timeEndNs: 2_000_000_000UL);

        var targetBytes = MinimalMatroskaBuilder.BuildSingleLocalOrderedChapter(
            segTarget,
            editionUid: targetEditionUid,
            timeStartNs: 0,
            timeEndNs: 3_000_000_000UL);

        var mainPath = WriteTemp(mainBytes);
        var targetPath = WriteTemp(targetBytes);
        try
        {
            var mainId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var targetItem = new Movie { Id = targetId, Path = targetPath };
            var mock = new Mock<IItemRepository>();
            mock.Setup(r => r.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery q) =>
                    q.SegmentUuid == Hex(segTarget)
                        ? new List<BaseItem> { targetItem }
                        : new List<BaseItem>());

            var sut = new OrderedChapterTimelineBuilder(mock.Object);
            var result = sut.Build(mainId, mainPath, NullLogger.Instance);

            Assert.Single(result.Parts);
            Assert.Equal(targetId, result.Parts[0].ItemId);
            Assert.False(result.Parts[0].IsMissing);
        }
        finally
        {
            File.Delete(mainPath);
            File.Delete(targetPath);
        }
    }

    [Fact]
    public void Cycle_BetweenTwoSegments_SecondExpansionSkipped()
    {
        var segA = new byte[16];
        segA[0] = 0x0a;
        var segB = new byte[16];
        segB[0] = 0x0b;
        const ulong editionUid = 1UL;

        var bytesA = MinimalMatroskaBuilder.BuildLinkedOrderedChapter(
            segA,
            editionUid,
            targetSegmentUid16: segB,
            targetEditionUid: editionUid,
            0,
            1_000_000_000UL);

        var bytesB = MinimalMatroskaBuilder.BuildLinkedOrderedChapter(
            segB,
            editionUid,
            targetSegmentUid16: segA,
            targetEditionUid: editionUid,
            0,
            1_000_000_000UL);

        var pathA = WriteTemp(bytesA);
        var pathB = WriteTemp(bytesB);
        try
        {
            var idA = Guid.NewGuid();
            var idB = Guid.NewGuid();
            var itemA = new Movie { Id = idA, Path = pathA };
            var itemB = new Movie { Id = idB, Path = pathB };
            var hexA = Hex(segA);
            var hexB = Hex(segB);

            var mock = new Mock<IItemRepository>();
            mock.Setup(r => r.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery q) =>
                {
                    if (q.SegmentUuid == hexB)
                    {
                        return new List<BaseItem> { itemB };
                    }

                    if (q.SegmentUuid == hexA)
                    {
                        return new List<BaseItem> { itemA };
                    }

                    return new List<BaseItem>();
                });

            var sut = new OrderedChapterTimelineBuilder(mock.Object);
            var result = sut.Build(idA, pathA, NullLogger.Instance);

            Assert.Empty(result.Parts);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }

    [Fact]
    public void Layout_UsesMainWhenMainContributes()
    {
        var seg = new byte[16];
        seg[0] = 0x06;
        var bytes = MinimalMatroskaBuilder.BuildTwoLocalOrderedChapters(
            seg,
            1,
            0,
            100_000_000,
            200_000_000,
            300_000_000);

        var path = WriteTemp(bytes);
        try
        {
            var id = Guid.NewGuid();
            var mock = new Mock<IItemRepository>();
            var sut = new OrderedChapterTimelineBuilder(mock.Object);
            var result = sut.Build(id, path, NullLogger.Instance);

            Assert.Equal(id, result.LayoutItemId);
            Assert.Equal(path, result.LayoutPath);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
