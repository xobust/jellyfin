using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.MediaEncoding.Keyframes.Matroska;
using Jellyfin.MediaEncoding.Keyframes.Matroska.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Streaming;
using Microsoft.Extensions.Logging;

namespace Jellyfin.MediaEncoding.Hls.OrderedChapters;

/// <summary>
/// Builds a virtual timeline from Matroska ordered chapters using the library SegmentUuid index.
/// </summary>
public sealed class OrderedChapterTimelineBuilder : IOrderedChapterTimelineBuilder
{
    /// <summary>
    /// Adjacent parts on the same source with a gap not larger than this are merged (100 ms).
    /// </summary>
    internal const long MergeThresholdTicks = 1_000_000L;

    private readonly IItemRepository _itemRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderedChapterTimelineBuilder"/> class.
    /// </summary>
    /// <param name="itemRepository">The item repository.</param>
    public OrderedChapterTimelineBuilder(IItemRepository itemRepository)
    {
        _itemRepository = itemRepository;
    }

    /// <inheritdoc />
    public OrderedChapterTimelineResult Build(Guid mainItemId, string mainItemPath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        MatroskaSegmentInfo mainInfo;
        try
        {
            mainInfo = MatroskaChapterExtractor.GetSegmentInfo(mainItemPath, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read Matroska metadata for ordered timeline: {Path}", mainItemPath);
            return new OrderedChapterTimelineResult(Array.Empty<OrderedTimelinePart>(), 0, mainItemId, mainItemPath);
        }

        var edition = SelectDefaultOrderedEdition(mainInfo.Editions);
        if (edition is null)
        {
            logger.LogDebug("No ordered non-hidden edition found for {Path}", mainItemPath);
            return new OrderedChapterTimelineResult(Array.Empty<OrderedTimelinePart>(), 0, mainItemId, mainItemPath);
        }

        var parts = new List<OrderedTimelinePart>();
        long missingTicks = 0;
        var guard = new HashSet<(string SegmentUuid, ulong EditionUid)>();

        ExpandEdition(mainItemId, mainItemPath, mainInfo, edition, guard, parts, ref missingTicks, logger);

        var merged = MergeAdjacentParts(parts);
        var (layoutId, layoutPath) = SelectLayout(mainItemId, mainItemPath, merged);
        return new OrderedChapterTimelineResult(merged, missingTicks, layoutId, layoutPath);
    }

    private static MatroskaEdition? SelectDefaultOrderedEdition(IReadOnlyList<MatroskaEdition> editions)
    {
        return editions
            .Where(e => !e.IsHidden && e.IsOrdered)
            .OrderByDescending(e => e.IsDefault)
            .ThenBy(e => e.EditionUid)
            .FirstOrDefault();
    }

    private static MatroskaEdition? SelectDefaultEditionForLinkedDuration(IReadOnlyList<MatroskaEdition> editions)
    {
        return editions
            .Where(e => !e.IsHidden)
            .OrderByDescending(e => e.IsDefault)
            .ThenBy(e => e.EditionUid)
            .FirstOrDefault();
    }

    private void ExpandEdition(
        Guid itemId,
        string path,
        MatroskaSegmentInfo segmentInfo,
        MatroskaEdition edition,
        HashSet<(string SegmentUuid, ulong EditionUid)> guard,
        List<OrderedTimelinePart> parts,
        ref long missingTicks,
        ILogger logger)
    {
        var segKey = segmentInfo.SegmentUuid ?? string.Empty;
        var key = (segKey, edition.EditionUid);
        if (!guard.Add(key))
        {
            logger.LogWarning(
                "Ordered chapter cycle detected for segment {Segment} edition {EditionUid}; skipping expansion",
                string.IsNullOrEmpty(segKey) ? "(none)" : segKey,
                edition.EditionUid);
            return;
        }

        try
        {
            foreach (var chapter in edition.Chapters)
            {
                ProcessChapter(chapter, itemId, path, guard, parts, ref missingTicks, logger);
            }
        }
        finally
        {
            guard.Remove(key);
        }
    }

    private void ProcessChapter(
        MatroskaChapter chapter,
        Guid currentItemId,
        string currentPath,
        HashSet<(string SegmentUuid, ulong EditionUid)> guard,
        List<OrderedTimelinePart> parts,
        ref long missingTicks,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(chapter.SegmentUuid))
        {
            if (!chapter.TimeEndTicks.HasValue)
            {
                logger.LogWarning("Skipping chapter without end time and without segment link");
                return;
            }

            parts.Add(new OrderedTimelinePart(
                currentItemId,
                currentPath,
                chapter.TimeStartTicks,
                chapter.TimeEndTicks.Value,
                false));
            return;
        }

        var target = ResolveBySegmentUuid(chapter.SegmentUuid);
        if (target is null)
        {
            RecordMissingChapter(chapter, null, currentItemId, currentPath, parts, ref missingTicks);
            return;
        }

        MatroskaSegmentInfo targetInfo;
        try
        {
            targetInfo = MatroskaChapterExtractor.GetSegmentInfo(target.Path, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read linked segment {Path}", target.Path);
            RecordMissingChapter(chapter, target, currentItemId, currentPath, parts, ref missingTicks);
            return;
        }

        if (!chapter.SegmentEditionUid.HasValue)
        {
            if (SelectDefaultEditionForLinkedDuration(targetInfo.Editions) is null)
            {
                logger.LogWarning("Linked target has no non-hidden edition for linked-duration mode: {Path}", target.Path);
                RecordMissingChapter(chapter, target, currentItemId, currentPath, parts, ref missingTicks);
                return;
            }

            if (!chapter.TimeEndTicks.HasValue)
            {
                logger.LogWarning("Linked-duration chapter missing end time; treating as missing");
                RecordMissingChapter(chapter, target, currentItemId, currentPath, parts, ref missingTicks);
                return;
            }

            parts.Add(new OrderedTimelinePart(
                target.Id,
                target.Path,
                chapter.TimeStartTicks,
                chapter.TimeEndTicks.Value,
                false));
            return;
        }

        var subEdition = targetInfo.Editions.FirstOrDefault(e => e.EditionUid == chapter.SegmentEditionUid.Value);
        if (subEdition is null)
        {
            logger.LogWarning(
                "Linked edition {EditionUid} not found on target {Path}",
                chapter.SegmentEditionUid.Value,
                target.Path);
            RecordMissingChapter(chapter, target, currentItemId, currentPath, parts, ref missingTicks);
            return;
        }

        ExpandEdition(target.Id, target.Path, targetInfo, subEdition, guard, parts, ref missingTicks, logger);
    }

    private BaseItem? ResolveBySegmentUuid(string segmentUuidHex)
    {
        var list = _itemRepository.GetItemList(new InternalItemsQuery
        {
            SegmentUuid = segmentUuidHex,
            Limit = 1
        });

        return list.Count > 0 ? list[0] : null;
    }

    private static void RecordMissingChapter(
        MatroskaChapter chapter,
        BaseItem? resolvedTarget,
        Guid fallbackItemId,
        string fallbackPath,
        List<OrderedTimelinePart> parts,
        ref long missingTicks)
    {
        var itemId = resolvedTarget?.Id ?? fallbackItemId;
        var path = resolvedTarget?.Path ?? fallbackPath;
        parts.Add(new OrderedTimelinePart(
            itemId,
            path,
            chapter.TimeStartTicks,
            chapter.TimeStartTicks,
            true));

        if (chapter.TimeEndTicks.HasValue)
        {
            missingTicks += chapter.TimeEndTicks.Value - chapter.TimeStartTicks;
        }
    }

    private static IReadOnlyList<OrderedTimelinePart> MergeAdjacentParts(IReadOnlyList<OrderedTimelinePart> parts)
    {
        if (parts.Count <= 1)
        {
            return parts;
        }

        var merged = new List<OrderedTimelinePart> { parts[0] };

        for (var i = 1; i < parts.Count; i++)
        {
            var prev = merged[^1];
            var cur = parts[i];

            if (!prev.IsMissing
                && !cur.IsMissing
                && prev.ItemId.Equals(cur.ItemId)
                && string.Equals(prev.Path, cur.Path, StringComparison.OrdinalIgnoreCase)
                && cur.SourceStartTicks >= prev.SourceEndTicks
                && (cur.SourceStartTicks - prev.SourceEndTicks) <= MergeThresholdTicks)
            {
                merged[^1] = prev with { SourceEndTicks = cur.SourceEndTicks };
            }
            else
            {
                merged.Add(cur);
            }
        }

        return merged;
    }

    private static (Guid ItemId, string Path) SelectLayout(
        Guid mainItemId,
        string mainItemPath,
        IReadOnlyList<OrderedTimelinePart> parts)
    {
        if (parts.Any(p => !p.IsMissing && p.ItemId.Equals(mainItemId)))
        {
            return (mainItemId, mainItemPath);
        }

        var first = parts.FirstOrDefault(p => !p.IsMissing);
        if (first is not null)
        {
            return (first.ItemId, first.Path);
        }

        return (mainItemId, mainItemPath);
    }
}
