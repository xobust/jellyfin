namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// One source slice on the ordered timeline.
/// </summary>
public sealed class OrderedChapterTranscodeSegment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderedChapterTranscodeSegment"/> class.
    /// </summary>
    /// <param name="path">Absolute path to the media file.</param>
    /// <param name="startTicks">Inclusive start in <see cref="System.TimeSpan"/> ticks (100 ns).</param>
    /// <param name="endTicks">Exclusive end in the same units.</param>
    public OrderedChapterTranscodeSegment(string path, long startTicks, long endTicks)
    {
        Path = path;
        StartTicks = startTicks;
        EndTicks = endTicks;
    }

    /// <summary>
    /// Gets the file path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets inclusive start in <see cref="System.TimeSpan"/> ticks (100 ns).
    /// </summary>
    public long StartTicks { get; }

    /// <summary>
    /// Gets exclusive end in the same units.
    /// </summary>
    public long EndTicks { get; }
}
