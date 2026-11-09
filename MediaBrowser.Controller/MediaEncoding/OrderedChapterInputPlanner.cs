using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Streaming;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Plans ffmpeg inputs for a requested virtual playback time on an ordered-chapter timeline.
/// </summary>
public static class OrderedChapterInputPlanner
{
    /// <summary>
    /// Builds the active segment set for a requested virtual timestamp.
    /// </summary>
    /// <param name="parts">Playable ordered timeline parts in virtual order.</param>
    /// <param name="requestedVirtualTicks">Requested virtual playback tick.</param>
    /// <returns>A planner result, or null when no playable part covers the request.</returns>
    public static OrderedChapterInputPlan? TryCreate(IReadOnlyList<OrderedTimelinePart> parts, long requestedVirtualTicks)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var playable = parts
            .Where(p => !p.IsMissing && p.SourceEndTicks > p.SourceStartTicks)
            .ToList();

        if (playable.Count == 0)
        {
            return null;
        }

        long virtualCursor = 0;
        var virtualRanges = new List<(OrderedTimelinePart Part, long VirtualStartTicks, long VirtualEndTicks)>();
        foreach (var part in playable)
        {
            var partDuration = part.SourceEndTicks - part.SourceStartTicks;
            var virtualStart = virtualCursor;
            var virtualEnd = virtualCursor + partDuration;
            virtualRanges.Add((part, virtualStart, virtualEnd));
            virtualCursor = virtualEnd;
        }

        var selectedIndex = 0;
        for (var i = 0; i < virtualRanges.Count; i++)
        {
            var current = virtualRanges[i];
            if (requestedVirtualTicks < current.VirtualEndTicks)
            {
                selectedIndex = i;
                break;
            }

            if (i == virtualRanges.Count - 1)
            {
                selectedIndex = i;
            }
        }

        var selected = virtualRanges[selectedIndex];
        var outputTsOffsetTicks = requestedVirtualTicks - selected.VirtualStartTicks;
        var selectedDuration = selected.Part.SourceEndTicks - selected.Part.SourceStartTicks;
        if (outputTsOffsetTicks > selectedDuration)
        {
            outputTsOffsetTicks = selectedDuration;
        }

        var activeParts = virtualRanges
            .Skip(selectedIndex)
            .Select(r => r.Part)
            .ToList();

        if (activeParts.Count == 0)
        {
            return null;
        }

        var inputs = new List<OrderedChapterInput>(activeParts.Count);
        for (var i = 0; i < activeParts.Count; i++)
        {
            var part = activeParts[i];
            var sourceStartTicks = i == 0
                ? part.SourceStartTicks + outputTsOffsetTicks
                : part.SourceStartTicks;

            inputs.Add(new OrderedChapterInput(part.Path, sourceStartTicks, part.SourceEndTicks));
        }

        var inputList = string.Concat(Enumerable.Range(0, inputs.Count).Select(i => $"[{i}:v:0][{i}:a:0]"));
        var graph = inputList + $"concat=n={inputs.Count}:v=1:a=1[ocv][oca]";

        return new OrderedChapterInputPlan(inputs, graph, outputTsOffsetTicks, requestedVirtualTicks, selectedIndex);
    }

    /// <summary>
    /// A single ffmpeg input for an ordered-chapter segment slice.
    /// </summary>
    public sealed record OrderedChapterInput(string Path, long SourceStartTicks, long SourceEndTicks);

    /// <summary>
    /// Output of the ordered-chapter input planner.
    /// </summary>
    public sealed record OrderedChapterInputPlan(
        IReadOnlyList<OrderedChapterInput> Inputs,
        string FilterComplexGraph,
        long OutputTsOffsetTicks,
        long RequestedVirtualTicks,
        int SelectedPartIndex);
}
