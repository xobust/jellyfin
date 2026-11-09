namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// FFmpeg input strategy for Matroska ordered-chapter virtual timelines.
/// </summary>
public enum OrderedChapterTranscodeMode
{
    /// <summary>
    /// No ordered-chapter transcode adjustments.
    /// </summary>
    None = 0,

    /// <summary>
    /// Use the ffmpeg concat demuxer with a generated ffconcat file for a single-file fast path.
    /// </summary>
    ConcatDemuxer = 1,

    /// <summary>
    /// Use per-input trim and a video-only concat filter graph.
    /// </summary>
    FilterComplexVideoConcat = 2
}
