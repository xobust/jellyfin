using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.MediaEncoding.Hls.Tests.OrderedChapters;

/// <summary>
/// Tests <see cref="OrderedChapterTranscodeCodecPolicy"/> used when ordered-chapter concat requires re-encode.
/// </summary>
public class OrderedChapterTranscodeCodecPolicyTests
{
    [Fact]
    public void GetTranscodeVideoCodec_SkipsLeadingCopy_UsesFirstNonCopy()
    {
        var state = new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            BaseRequest = new VideoRequestDto { VideoCodec = "copy,h264,hevc" },
            OutputContainer = "mp4"
        };

        var codec = OrderedChapterTranscodeCodecPolicy.GetTranscodeVideoCodec(state, _ => "copy");

        Assert.Equal("h264", codec);
    }

    [Fact]
    public void GetTranscodeVideoCodec_AllCopyInRequest_UsesInferThenH264WhenInferReturnsCopy()
    {
        var state = new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            BaseRequest = new VideoRequestDto { VideoCodec = "copy" },
            OutputContainer = "mp4"
        };

        var codec = OrderedChapterTranscodeCodecPolicy.GetTranscodeVideoCodec(state, _ => "copy");

        Assert.Equal("h264", codec);
    }

    [Fact]
    public void GetTranscodeVideoCodec_InferReturnsHevc_UsesHevc()
    {
        var state = new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            OutputContainer = "mkv"
        };

        var codec = OrderedChapterTranscodeCodecPolicy.GetTranscodeVideoCodec(state, _ => "hevc");

        Assert.Equal("hevc", codec);
    }

    [Fact]
    public void GetTranscodeAudioCodec_CopyOutput_UsesSupportedNonCopy()
    {
        var state = new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            OutputAudioCodec = "copy",
            OutputContainer = "mp4",
            SupportedAudioCodecs = ["copy", "aac", "mp3"],
            AudioStream = new MediaStream { Type = MediaStreamType.Audio, Codec = "aac" }
        };

        var codec = OrderedChapterTranscodeCodecPolicy.GetTranscodeAudioCodec(state, () => "aac");

        Assert.Equal("aac", codec);
    }

    [Fact]
    public void GetTranscodeAudioCodec_NoAudioStream_ReturnsAac()
    {
        var state = new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            OutputAudioCodec = "copy"
        };

        var codec = OrderedChapterTranscodeCodecPolicy.GetTranscodeAudioCodec(state, () => "mp3");

        Assert.Equal("aac", codec);
    }
}
