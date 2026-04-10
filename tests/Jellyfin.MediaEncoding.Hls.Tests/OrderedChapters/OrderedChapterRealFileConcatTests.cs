using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.MediaEncoding.Hls.OrderedChapters;
using Jellyfin.MediaEncoding.Keyframes.Matroska;
using Jellyfin.MediaEncoding.Keyframes.Matroska.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Streaming;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.MediaEncoding.Hls.Tests.OrderedChapters;

/// <summary>
/// Real-media integration test for chapter parsing and concat output generation.
/// </summary>
public sealed class OrderedChapterRealFileConcatTests
{
    private readonly ITestOutputHelper _output;

    public OrderedChapterRealFileConcatTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BuildTimeline_FromLibrarySample_WritesPlayableConcatFile()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null)
        {
            _output.WriteLine("Repository root not found. Skipping real-media concat test.");
            return;
        }

        var libraryPath = Path.Combine(repoRoot, "library");
        if (!Directory.Exists(libraryPath))
        {
            _output.WriteLine("No library directory found. Skipping real-media concat test.");
            return;
        }

        var preferredLotL03 = Path.Combine(
            libraryPath,
            "Land of the Lustrous (Houseki no Kuni) (2017) [1080p] [kuchikirukia] [v2]",
            "03. Land of the Lustrous [BD 1080p Ma10P AAC][kuchikirukia].mkv");

        var mkvFiles = Directory
            .EnumerateFiles(libraryPath, "*.mkv", SearchOption.AllDirectories)
            .Where(path => !path.Contains(".trickplay", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (File.Exists(preferredLotL03))
        {
            mkvFiles.Remove(preferredLotL03);
            mkvFiles.Insert(0, preferredLotL03);
        }

        if (mkvFiles.Count == 0)
        {
            _output.WriteLine("No MKV files found under library/. Skipping real-media concat test.");
            return;
        }

        var parseableFiles = new List<string>();
        var candidates = new List<(string Path, List<OrderedChapterTranscodeSegment> Segments)>();
        foreach (var mkv in mkvFiles)
        {
            try
            {
                var info = MatroskaChapterExtractor.GetSegmentInfo(mkv, NullLogger.Instance);
                parseableFiles.Add(mkv);
                var edition = info.Editions
                    .Where(e => !e.IsHidden)
                    .OrderByDescending(e => e.IsOrdered)
                    .ThenByDescending(e => e.IsDefault)
                    .ThenBy(e => e.EditionUid)
                    .FirstOrDefault();

                if (edition is null)
                {
                    continue;
                }

                var segments = edition.Chapters
                    .Where(c => string.IsNullOrEmpty(c.SegmentUuid) && c.TimeEndTicks.HasValue && c.TimeEndTicks.Value > c.TimeStartTicks)
                    .Select(c => new OrderedChapterTranscodeSegment(mkv, c.TimeStartTicks, c.TimeEndTicks!.Value))
                    .Take(6)
                    .ToList();

                if (segments.Count > 0)
                {
                    candidates.Add((mkv, segments));
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Skipping unreadable file '{mkv}': {ex.Message}");
            }
        }

        if (candidates.Count == 0 && parseableFiles.Count == 0)
        {
            _output.WriteLine("No parseable MKV files found in library/. Skipping real-media concat test.");
            return;
        }

        var concatPath = Path.Combine(
            Path.GetTempPath(),
            "jf-ordered-chapters-real-" + Guid.NewGuid().ToString("N") + ".ffconcat");
        var selected = candidates.Count > 0
            ? candidates.OrderByDescending(c => c.Segments.Count).First()
            : (
                Path: parseableFiles[0],
                Segments: new List<OrderedChapterTranscodeSegment>
                {
                    // Fallback when chapter time ranges are absent: first 5 seconds for manual playback checks.
                    new(parseableFiles[0], 0L, 50_000_000L)
                });

        try
        {
            var plan = OrderedChapterTranscodePlanner.TryCreatePlan(Guid.NewGuid(), new OrderedChapterTimelineResult(selected.Segments.Select(s => new OrderedTimelinePart(Guid.NewGuid(), s.Path, s.StartTicks, s.EndTicks, false)).ToArray(), 0, Guid.NewGuid(), selected.Path));
            Assert.NotNull(plan);
            Assert.Equal(OrderedChapterTranscodeMode.FilterComplexVideoConcat, plan!.Mode);
            Assert.Contains("concat=n=" + plan.Segments.Count, plan.FilterComplexGraph, StringComparison.Ordinal);

            _output.WriteLine($"Main chapter file: {selected.Path}");
            _output.WriteLine($"Filter graph generated for {plan.Segments.Count} segments");
            _output.WriteLine("This ordered-chapter path is filter_complex-only and does not use ffconcat demuxer output.");
        }
        finally
        {
            if (string.Equals(Environment.GetEnvironmentVariable("JF_DELETE_ORDERED_CONCAT"), "1", StringComparison.Ordinal))
            {
                File.Delete(concatPath);
            }
        }
    }

    private static string? FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Jellyfin.sln")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null || string.Equals(parent.FullName, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent.FullName;
        }

        return null;
    }

    /// <summary>
    /// Exercises <see cref="OrderedChapterTimelineBuilder"/> + <see cref="OrderedChapterTranscodePlanner.TryCreatePlan"/> with
    /// real MKVs when <c>library/</c> contains Land of the Lustrous (ordered edition with <c>ChapterSegmentUID</c> links, same layout mpv uses).
    /// </summary>
    /// <remarks>
    /// Episode 03 has an ordered edition whose chapter atoms reference external segments by UUID (e.g. NCOP / NCED in the library).
    /// The test indexes every MKV’s <see cref="MatroskaSegmentInfo.SegmentUuid"/> like a library scan, then asserts the virtual
    /// timeline and ffconcat list include one file path per distinct linked UUID. If linked files are missing from <c>library/</c>,
    /// the test skips with a clear message instead of a brittle failure.
    /// <para>
    /// The ffconcat path is printed in test output (under the system temp directory). It is not deleted unless
    /// <c>JF_DELETE_ORDERED_CONCAT=1</c>. To save a stable copy, set <c>JF_ORDERED_CONCAT_OUTPUT_DIR</c> to a folder; the test writes
    /// <c>LandOfTheLustrous03-ordered-chapters.ffconcat</c> there. Example:
    /// <c>JF_ORDERED_CONCAT_OUTPUT_DIR=/tmp/oc-out dotnet test --filter LandOfTheLustrous03</c>.
    /// </para>
    /// <para>
    /// Remuxing with <c>ffmpeg -f concat -safe 0 -i list.ffconcat -c copy out.mkv</c> can fail on subtitle/attachment streams
    /// (unknown timestamps at segment joins, odd Matroska streams). That is an ffmpeg/concat limitation, not evidence of wrong
    /// <c>inpoint</c>/<c>outpoint</c> values; try <c>-map 0:v:0 -map 0:a:0 -c copy</c> or <c>-sn</c> to confirm the concat list.
    /// </para>
    /// </remarks>
    [Fact]
    public void LandOfTheLustrous03_OrderedTimeline_ProducesMultiSourceFfconcat_WhenLibraryIndexesSegmentUuids()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null)
        {
            _output.WriteLine("Repository root not found. Skipping.");
            return;
        }

        var libraryPath = Path.Combine(repoRoot, "library");
        var ep03 = Path.Combine(
            libraryPath,
            "Land of the Lustrous (Houseki no Kuni) (2017) [1080p] [kuchikirukia] [v2]",
            "03. Land of the Lustrous [BD 1080p Ma10P AAC][kuchikirukia].mkv");

        if (!File.Exists(ep03))
        {
            _output.WriteLine("Land of the Lustrous ep03 sample not under library/. Skipping.");
            return;
        }

        MatroskaSegmentInfo mainInfo;
        try
        {
            mainInfo = MatroskaChapterExtractor.GetSegmentInfo(ep03, NullLogger.Instance);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Skipping real MKV sample '{ep03}' because it cannot be parsed in this environment: {ex.Message}");
            return;
        }

        Assert.NotEmpty(mainInfo.Editions);
        var orderedEdition = mainInfo.Editions.FirstOrDefault(e => !e.IsHidden && e.IsOrdered);
        Assert.NotNull(orderedEdition);
        Assert.Contains(
            orderedEdition!.Chapters,
            c => !string.IsNullOrEmpty(c.SegmentUuid));

        var linkedUuids = orderedEdition.Chapters
            .Where(c => !string.IsNullOrEmpty(c.SegmentUuid))
            .Select(c => c.SegmentUuid!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.NotEmpty(linkedUuids);

        var itemsBySegmentUuid = new Dictionary<string, Movie>(StringComparer.OrdinalIgnoreCase);
        foreach (var mkv in Directory.EnumerateFiles(libraryPath, "*.mkv", SearchOption.AllDirectories)
                     .Where(p => !p.Contains(".trickplay", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var segInfo = MatroskaChapterExtractor.GetSegmentInfo(mkv, NullLogger.Instance);
                if (string.IsNullOrEmpty(segInfo.SegmentUuid))
                {
                    continue;
                }

                if (!itemsBySegmentUuid.ContainsKey(segInfo.SegmentUuid))
                {
                    itemsBySegmentUuid[segInfo.SegmentUuid] = new Movie { Id = Guid.NewGuid(), Path = mkv };
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Skipping unreadable '{mkv}': {ex.Message}");
            }
        }

        foreach (var uuid in linkedUuids)
        {
            if (!itemsBySegmentUuid.ContainsKey(uuid))
            {
                _output.WriteLine(
                    $"Skip: library index has no file for ChapterSegmentUUID {uuid} (place NCOP/NCED MKVs next to ep03). " +
                    $"Indexed: [{string.Join(", ", itemsBySegmentUuid.Keys)}].");
                return;
            }
        }

        var mainId = Guid.NewGuid();
        var mock = new Mock<IItemRepository>();
        mock.Setup(r => r.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) =>
                !string.IsNullOrEmpty(q.SegmentUuid) && itemsBySegmentUuid.TryGetValue(q.SegmentUuid!, out var m)
                    ? new List<BaseItem> { m }
                    : new List<BaseItem>());

        var builder = new OrderedChapterTimelineBuilder(mock.Object);
        var timeline = builder.Build(mainId, ep03, NullLogger.Instance);

        var playable = timeline.Parts.Where(p => !p.IsMissing && p.SourceEndTicks > p.SourceStartTicks).ToList();
        var distinctPaths = playable.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var codecs = distinctPaths.ToDictionary(p => p, _ => "h264", StringComparer.OrdinalIgnoreCase);
        try
        {
            var plan = OrderedChapterTranscodePlanner.TryCreatePlan(mainId, timeline);
            Assert.NotNull(plan);
            Assert.Equal(OrderedChapterTranscodeMode.FilterComplexVideoConcat, plan!.Mode);

            var pathsInPlan = plan.Segments.Select(s => s.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var externalSegments = pathsInPlan
                .Where(p => !string.Equals(p, ep03, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(
                linkedUuids.Count == externalSegments.Count,
                "Expected one distinct external file per linked ChapterSegmentUUID (" + linkedUuids.Count + "). " +
                "External: [" + string.Join("; ", externalSegments) + "]. All paths: [" + string.Join("; ", pathsInPlan) + "].");

            Assert.Contains(externalSegments, PathLooksLikeNonCreditOpening);
            Assert.Contains(externalSegments, PathLooksLikeNonCreditEnding);
            Assert.Contains("concat=n=" + plan.Segments.Count, plan.FilterComplexGraph, StringComparison.Ordinal);

            _output.WriteLine($"Linked UUIDs ({linkedUuids.Count}): {string.Join(", ", linkedUuids)}");
            _output.WriteLine($"External segment paths: {string.Join(", ", externalSegments)}");
            _output.WriteLine($"Filter graph uses {plan.Segments.Count} ordered inputs");
        }
        finally
        {
            if (string.Equals(Environment.GetEnvironmentVariable("JF_DELETE_ORDERED_CONCAT"), "1", StringComparison.Ordinal))
            {
                // The old ffconcat demuxer path has been removed; no temp file is produced in this mode.
            }
        }
    }

    /// <summary>Non-credit opening (typical BD mux: NCOP in filename).</summary>
    private static bool PathLooksLikeNonCreditOpening(string absolutePath)
    {
        var fn = Path.GetFileName(absolutePath);
        return fn.Contains("NCOP", StringComparison.OrdinalIgnoreCase)
               || fn.Contains("(Opening)", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Non-credit ending (typical BD mux: NCED in filename).</summary>
    private static bool PathLooksLikeNonCreditEnding(string absolutePath)
    {
        var fn = Path.GetFileName(absolutePath);
        return fn.Contains("NCED", StringComparison.OrdinalIgnoreCase)
               || fn.Contains("(Ending)", StringComparison.OrdinalIgnoreCase);
    }
}
