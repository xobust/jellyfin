using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.MediaEncoding.Keyframes.Matroska;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.MediaEncoding.Hls.Tests.OrderedChapters;

/// <summary>
/// End-to-end ffmpeg validation for a generated ordered-chapter MKV fixture.
/// </summary>
public sealed class OrderedChapterFfmpegTests
{
    private const int AudioSampleRate = 44100;

    [FfmpegFact]
    public void OrderedChapterFixture_GeneratesPlayableConcatOutput_WithSystemFfmpeg()
    {
        var repoRoot = FindRepositoryRoot();
        Assert.NotNull(repoRoot);

        var fixtureDir = Path.Combine(Path.GetTempPath(), "jf-ordered-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDir);

        try
        {
            var script = Path.Combine(repoRoot!, "tests", "fixtures", "ordered", "make.sh");
            var result = RunProcess("bash", script, fixtureDir);
            Assert.Equal(0, result.ExitCode);

            var mainMkv = Path.Combine(fixtureDir, "ep01.mkv");
            var segmentInfo = MatroskaChapterExtractor.GetSegmentInfo(mainMkv, NullLogger.Instance);
            var orderedEdition = segmentInfo.Editions.FirstOrDefault(e => !e.IsHidden && e.IsOrdered);
            Assert.NotNull(orderedEdition);
            Assert.Equal(3, orderedEdition!.Chapters.Count);

            var sourceFiles = new[]
            {
                (Path: mainMkv, Id: Guid.NewGuid(), Uuid: segmentInfo.SegmentUuid),
                (Path: Path.Combine(fixtureDir, "OP.mkv"), Id: Guid.NewGuid(), Uuid: MatroskaChapterExtractor.GetSegmentInfo(Path.Combine(fixtureDir, "OP.mkv"), NullLogger.Instance).SegmentUuid),
                (Path: Path.Combine(fixtureDir, "ED.mkv"), Id: Guid.NewGuid(), Uuid: MatroskaChapterExtractor.GetSegmentInfo(Path.Combine(fixtureDir, "ED.mkv"), NullLogger.Instance).SegmentUuid)
            };
            var sourcesByUuid = sourceFiles.ToDictionary(source => source.Uuid!, StringComparer.OrdinalIgnoreCase);
            var timelineParts = orderedEdition.Chapters.Select(chapter =>
            {
                var source = sourcesByUuid[chapter.SegmentUuid ?? segmentInfo.SegmentUuid!];
                return new OrderedTimelinePart(source.Id, source.Path, chapter.TimeStartTicks, chapter.TimeEndTicks!.Value, false);
            }).ToArray();
            Assert.Equal(50_000_000L, timelineParts[0].SourceEndTicks);
            Assert.Equal(33_000_000L, timelineParts[1].SourceStartTicks);
            Assert.Equal(177_000_000L, timelineParts[1].SourceEndTicks);
            Assert.Equal(49_600_000L, timelineParts[2].SourceEndTicks);

            var plan = OrderedChapterTranscodePlanner.TryCreatePlan(
                Guid.NewGuid(),
                new OrderedChapterTimelineResult(
                    timelineParts,
                    0,
                    Guid.NewGuid(),
                    mainMkv));

            Assert.NotNull(plan);
            Assert.Equal(OrderedChapterTranscodeMode.FilterComplexVideoConcat, plan!.Mode);
            Assert.Equal(3, plan.Segments.Count);

            var playlist = Path.Combine(fixtureDir, "ordered.m3u8");
            var segmentPattern = Path.Combine(fixtureDir, "segment%02d.ts");
            var filter = "[0:v:0][0:a:0][1:v:0][1:a:0][2:v:0][2:a:0]concat=n=3:v=1:a=1[v][a]";
            var ffmpegArguments = new List<string> { "-y" };
            foreach (var segment in plan.Segments)
            {
                ffmpegArguments.AddRange(
                [
                    "-ss",
                    (segment.StartTicks / (double)TimeSpan.TicksPerSecond).ToString("0.#########", CultureInfo.InvariantCulture),
                    "-t",
                    ((segment.EndTicks - segment.StartTicks) / (double)TimeSpan.TicksPerSecond).ToString("0.#########", CultureInfo.InvariantCulture),
                    "-i",
                    segment.Path
                ]);
            }

            ffmpegArguments.AddRange(
            [
                "-filter_complex",
                filter,
                "-map",
                "[v]",
                "-map",
                "[a]",
                "-c:v",
                "libx264",
                "-preset",
                "ultrafast",
                "-pix_fmt",
                "yuv420p",
                "-force_key_frames",
                "expr:gte(t,n_forced*6)",
                "-c:a",
                "aac",
                "-f",
                "hls",
                "-hls_time",
                "6",
                "-hls_segment_filename",
                segmentPattern,
                playlist
            ]);
            var ffmpegResult = RunProcess("ffmpeg", ffmpegArguments.ToArray());

            Assert.Equal(0, ffmpegResult.ExitCode);
            var segments = File.ReadAllLines(playlist)
                .Where(line => line.EndsWith(".ts", StringComparison.Ordinal))
                .Select(line => Path.Combine(fixtureDir, line))
                .ToArray();
            Assert.Equal(5, segments.Length);

            var durations = File.ReadAllLines(playlist)
                .Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal))
                .Select(line => double.Parse(line.AsSpan("#EXTINF:".Length).TrimEnd(','), CultureInfo.InvariantCulture))
                .ToArray();
            Assert.Equal(segments.Length, durations.Length);
            Assert.InRange(durations.Sum(), 24.1, 24.6);

            AssertSamples(fixtureDir, playlist, new[]
            {
                (2.0, 0, 440.0),
                (10.0, 1, 880.0),
                (22.0, 2, 1320.0)
            });

            AssertMonotonicSegmentTimestamps(segments);
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    private static void AssertSamples(
        string fixtureDir,
        string playlist,
        (double Time, int DominantChannel, double Frequency)[] samples)
    {
        var videoPath = Path.Combine(fixtureDir, "ordered.rgb");
        var videoResult = RunProcess(
            "ffmpeg",
            "-v",
            "error",
            "-i",
            playlist,
            "-f",
            "rawvideo",
            "-pix_fmt",
            "rgb24",
            videoPath);
        Assert.Equal(0, videoResult.ExitCode);
        var frameSize = 320 * 240 * 3;
        var frameCount = (int)(new FileInfo(videoPath).Length / frameSize);

        var audioPath = Path.Combine(fixtureDir, "ordered.f32");
        var audioResult = RunProcess(
            "ffmpeg",
            "-v",
            "error",
            "-i",
            playlist,
            "-vn",
            "-ac",
            "1",
            "-ar",
            AudioSampleRate.ToString(CultureInfo.InvariantCulture),
            "-f",
            "f32le",
            audioPath);
        Assert.Equal(0, audioResult.ExitCode);

        foreach (var sample in samples)
        {
            var frameIndex = (int)(sample.Time * 24);
            Assert.InRange(frameIndex, 0, frameCount - 1);
            var rgb = new byte[frameSize];
            using (var stream = File.OpenRead(videoPath))
            {
                stream.Position = (long)frameIndex * frameSize;
                Assert.Equal(frameSize, stream.Read(rgb));
            }

            var means = new double[3];
            for (var index = 0; index < rgb.Length; index++)
            {
                means[index % 3] += rgb[index];
            }

            var dominantChannel = Array.IndexOf(means, means.Max());
            Assert.True(
                sample.DominantChannel == dominantChannel,
                $"Expected channel {sample.DominantChannel} at {sample.Time}s but got {dominantChannel}; means: {string.Join(", ", means)}.");

            var audioStart = (long)(sample.Time * AudioSampleRate);
            var crossings = CountPositiveZeroCrossings(audioPath, audioStart, AudioSampleRate * 2 / 5);
            var estimatedFrequency = crossings / 0.4;
            Assert.InRange(estimatedFrequency, sample.Frequency * 0.9, sample.Frequency * 1.1);
        }
    }

    private static int CountPositiveZeroCrossings(string path, long startSample, int sampleCount)
    {
        using var stream = File.OpenRead(path);
        stream.Position = startSample * sizeof(float);
        using var reader = new BinaryReader(stream);
        var crossings = 0;
        var previous = reader.ReadSingle();
        for (var index = 1; index < sampleCount && stream.Position + sizeof(float) <= stream.Length; index++)
        {
            var current = reader.ReadSingle();
            if (previous <= 0 && current > 0)
            {
                crossings++;
            }

            previous = current;
        }

        return crossings;
    }

    private static void AssertMonotonicSegmentTimestamps(string[] segments)
    {
        double? previousTimestamp = null;
        foreach (var segment in segments)
        {
            var probe = RunProcess(
                "ffprobe",
                "-v",
                "error",
                "-select_streams",
                "v:0",
                "-show_entries",
                "frame=best_effort_timestamp_time",
                "-of",
                "csv=p=0",
                segment);
            Assert.Equal(0, probe.ExitCode);
            var timestamps = probe.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => double.Parse(line.Trim().TrimEnd(','), CultureInfo.InvariantCulture))
                .ToArray();
            Assert.NotEmpty(timestamps);
            Assert.Equal(timestamps.OrderBy(timestamp => timestamp), timestamps);
            if (previousTimestamp.HasValue)
            {
                Assert.True(timestamps[0] >= previousTimestamp.Value, "HLS segment timestamps must not move backwards.");
            }

            previousTimestamp = timestamps[^1];
        }
    }

    private static string? FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Jellyfin.sln"))
                || File.Exists(Path.Combine(current, "Jellyfin.slnx")))
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

    private static (int ExitCode, string StandardOutput, string StandardError) RunProcess(string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }
}
