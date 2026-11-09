using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.MediaEncoding.Hls.Tests.OrderedChapters;

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (FindExecutable("bash") is null
            || FindExecutable("ffmpeg") is null
            || FindExecutable("ffprobe") is null
            || FindExecutable("mkvmerge") is null)
        {
            Skip = "Requires bash, ffmpeg, ffprobe, and mkvmerge on PATH.";
        }
    }

    private static string? FindExecutable(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        return path?.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, executable))
            .FirstOrDefault(File.Exists);
    }
}
