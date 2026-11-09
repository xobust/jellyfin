#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Controller.MediaEncoding;

public partial class EncodingHelper
{
    private bool TryAppendOrderedChapterMainInput(
        EncodingJobInfo state,
        EncodingOptions options,
        string segmentContainer,
        StringBuilder arg,
        out bool orderedHwIncompatible)
    {
        orderedHwIncompatible = false;

        if (!ShouldAttemptOrderedChapterTranscode(state, segmentContainer))
        {
            return false;
        }

        OrderedChapterTimelineResult timeline;
        try
        {
            timeline = _orderedChapterTimelineBuilder.Build(state.LibraryItemId, state.LibraryItemPath, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ordered chapter timeline build failed for {Path}", state.LibraryItemPath);
            return false;
        }

        if (timeline.MissingTicks > 0)
        {
            _logger.LogWarning(
                "Ordered chapter timeline has {MissingMs} ms of unresolved virtual duration (missing linked segments)",
                timeline.MissingTicks / 10000L);
        }

        var pairedForProbe = timeline.Parts
            .Where(p => !p.IsMissing && p.SourceEndTicks > p.SourceStartTicks)
            .ToList();

        if (pairedForProbe.Count == 0)
        {
            return false;
        }

        var needsOrdered = pairedForProbe.Count > 1
                           || pairedForProbe.Any(p => !p.ItemId.Equals(state.LibraryItemId));
        if (!needsOrdered)
        {
            return false;
        }

        var requestedVirtualTicks = state.StartTimeTicks ?? 0L;
        var plan = OrderedChapterTranscodePlanner.TryCreatePlan(
            state.LibraryItemId,
            timeline,
            requestedVirtualTicks);

        if (plan is null)
        {
            return false;
        }

        if (plan.Mode == OrderedChapterTranscodeMode.FilterComplexVideoConcat)
        {
            if (!string.IsNullOrEmpty(GetInputVideoHwaccelArgs(state, options)))
            {
                _logger.LogWarning(
                    "Ordered chapter transcode: filter_complex concat is not used with hardware decoding; using primary file input.");
                return false;
            }

            _logger.LogWarning(
                "Ordered chapter transcode: video codecs differ between timeline parts; using filter_complex concat.");

            AppendFilterComplexOrderedInputs(state, arg, plan, timeline);
            orderedHwIncompatible = true;
            return true;
        }

        return false;
    }

    private void AppendFilterComplexOrderedInputs(
        EncodingJobInfo state,
        StringBuilder arg,
        OrderedChapterTranscodePlan plan,
        OrderedChapterTimelineResult timeline)
    {
        var segments = plan.Segments;
        var n = segments.Count;
        var graph = plan.FilterComplexGraph ?? string.Empty;

        foreach (var seg in segments)
        {
            var ss = _mediaEncoder.GetTimeParameter(seg.StartTicks);
            var to = _mediaEncoder.GetTimeParameter(seg.EndTicks);
            arg.Append(" -ss ")
                .Append(ss)
                .Append(" -to ")
                .Append(to)
                .Append(" -i ")
                .Append(_mediaEncoder.GetInputPathArgument(seg.Path, state.MediaSource));
        }

        arg.Append(" -filter_complex \"")
            .Append(graph)
            .Append('"');

        if (plan.OutputTsOffsetTicks > 0)
        {
            arg.Append(" -output_ts_offset ")
                .Append(_mediaEncoder.GetTimeParameter(plan.OutputTsOffsetTicks));
        }

        arg.Append(" -i ")
            .Append(_mediaEncoder.GetInputPathArgument(timeline.LayoutPath, state.MediaSource));

        state.OrderedChapterInputMode = OrderedChapterTranscodeMode.FilterComplexVideoConcat;
        state.OrderedChapterVideoMapLabel = OrderedChapterTranscodePlanner.VideoConcatOutputLabel;
        state.OrderedChapterLayoutInputIndex = n;
    }

    private void ResetOrderedChapterState(EncodingJobInfo state)
    {
        state.OrderedChapterInputMode = OrderedChapterTranscodeMode.None;
        state.OrderedChapterVideoMapLabel = null;
        state.OrderedChapterLayoutInputIndex = -1;
    }

    private static bool IsMatroskaLikeContainer(string container)
    {
        if (string.IsNullOrEmpty(container))
        {
            return false;
        }

        var c = container.TrimStart('.');
        return string.Equals(c, "mkv", StringComparison.OrdinalIgnoreCase)
               || string.Equals(c, "matroska", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldAttemptOrderedChapterTranscode(EncodingJobInfo state, string segmentContainer)
    {
        if (_orderedChapterTimelineBuilder is null
            || !state.HasOrderedChapters
            || !state.IsInputVideo
            || state.VideoStream is null
            || string.IsNullOrEmpty(state.LibraryItemPath))
        {
            return false;
        }

        if (!IsMatroskaLikeContainer(state.InputContainer)
            || state.InputProtocol != MediaProtocol.File
            || state.MediaSource is null)
        {
            return false;
        }

        if (state.MediaSource.VideoType != VideoType.VideoFile)
        {
            return false;
        }

        if (state.AudioStream?.IsExternal == true)
        {
            return false;
        }

        if (ShouldEncodeSubtitle(state))
        {
            return false;
        }

        if (state.SubtitleStream is not null
            && state.SubtitleStream.IsExternal
            && !state.SubtitleStream.IsTextSubtitleStream)
        {
            return false;
        }

        return true;
    }
}
