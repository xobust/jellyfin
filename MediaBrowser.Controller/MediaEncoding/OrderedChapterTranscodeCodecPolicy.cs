using System;
using System.Linq;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// Chooses output codecs when Matroska ordered-chapter virtual timelines use the concat demuxer or filter_complex concat:
/// stream copy is unreliable; prefer explicit transcode codecs derived from the client request.
/// </summary>
public static class OrderedChapterTranscodeCodecPolicy
{
    /// <summary>
    /// Resolves a non-copy video codec from the streaming request, falling back to <paramref name="inferFromUrl"/> then <c>h264</c>.
    /// </summary>
    /// <param name="state">Current encoding job.</param>
    /// <param name="inferFromUrl">Maps a synthetic URL (e.g. stream.mp4) to a default codec.</param>
    /// <returns>Codec id suitable for ffmpeg <c>-codec:v</c>.</returns>
    public static string GetTranscodeVideoCodec(EncodingJobInfo state, Func<string, string> inferFromUrl)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inferFromUrl);

        if (state.BaseRequest is not null && !string.IsNullOrEmpty(state.BaseRequest.VideoCodec))
        {
            foreach (var part in state.BaseRequest.VideoCodec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!EncodingHelper.IsCopyCodec(part))
                {
                    return part;
                }
            }
        }

        var ext = string.IsNullOrEmpty(state.OutputContainer) ? ".mp4" : "." + state.OutputContainer.TrimStart('.');
        var inferred = inferFromUrl("stream" + ext);
        return EncodingHelper.IsCopyCodec(inferred) ? "h264" : inferred;
    }

    /// <summary>
    /// Resolves a non-copy audio codec from supported codecs / request, falling back to <paramref name="inferFromContainer"/>.
    /// </summary>
    /// <param name="state">Current encoding job.</param>
    /// <param name="inferFromContainer">Infers a default codec from output container (e.g. mp4 → aac).</param>
    /// <returns>Codec id suitable for ffmpeg <c>-codec:a</c>.</returns>
    public static string GetTranscodeAudioCodec(EncodingJobInfo state, Func<string> inferFromContainer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inferFromContainer);

        if (state.AudioStream is null)
        {
            return "aac";
        }

        if (!string.IsNullOrEmpty(state.OutputAudioCodec) && !EncodingHelper.IsCopyCodec(state.OutputAudioCodec))
        {
            return state.OutputAudioCodec;
        }

        if (state.SupportedAudioCodecs is not null)
        {
            var nonCopy = state.SupportedAudioCodecs.FirstOrDefault(c => !EncodingHelper.IsCopyCodec(c));
            if (!string.IsNullOrEmpty(nonCopy))
            {
                return nonCopy;
            }
        }

        if (state.BaseRequest is not null
            && !string.IsNullOrEmpty(state.BaseRequest.AudioCodec)
            && !EncodingHelper.IsCopyCodec(state.BaseRequest.AudioCodec))
        {
            return state.BaseRequest.AudioCodec;
        }

        var inferred = inferFromContainer();
        return EncodingHelper.IsCopyCodec(inferred) ? "aac" : inferred;
    }
}
