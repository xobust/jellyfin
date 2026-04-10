using System;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Controller.Streaming;

/// <summary>
/// Builds a virtual playback timeline from Matroska ordered chapters and library segment links.
/// </summary>
public interface IOrderedChapterTimelineBuilder
{
    /// <summary>
    /// Parses the main file and resolves linked segments via <see cref="MediaBrowser.Controller.Persistence.IItemRepository"/> (SegmentUuid index).
    /// </summary>
    /// <param name="mainItemId">Library id of the primary item.</param>
    /// <param name="mainItemPath">Absolute path to the primary Matroska file.</param>
    /// <param name="logger">Logger for warnings (cycles, missing links).</param>
    /// <returns>Timeline parts and layout selection; empty parts if no suitable ordered edition exists.</returns>
    OrderedChapterTimelineResult Build(Guid mainItemId, string mainItemPath, ILogger logger);
}
