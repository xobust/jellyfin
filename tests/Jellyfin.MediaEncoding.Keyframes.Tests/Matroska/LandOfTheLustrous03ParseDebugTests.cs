using System;
using System.IO;
using System.Linq;
using Jellyfin.MediaEncoding.Keyframes.Matroska;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.MediaEncoding.Keyframes.Tests.Matroska;

/// <summary>
/// Optional debug tests for real files under repo <c>library/</c> (not run in CI without those assets).
/// </summary>
public sealed class LandOfTheLustrous03ParseDebugTests
{
    private readonly ITestOutputHelper _output;

    public LandOfTheLustrous03ParseDebugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Episode03_GetSegmentInfo_DoesNotThrow()
    {
        var path = Path.Combine(
            FindRepoRoot() ?? string.Empty,
            "library",
            "Land of the Lustrous (Houseki no Kuni) (2017) [1080p] [kuchikirukia] [v2]",
            "03. Land of the Lustrous [BD 1080p Ma10P AAC][kuchikirukia].mkv");

        if (!File.Exists(path))
        {
            _output.WriteLine("Sample not present; skipping.");
            return;
        }

        var info = MatroskaChapterExtractor.GetSegmentInfo(path, NullLogger.Instance);
        Assert.Equal("bbd95d66bab64fdf2e7d440fdbac2bc2", info.SegmentUuid);
        Assert.NotEmpty(info.Editions);
        var ordered = info.Editions.FirstOrDefault(e => !e.IsHidden && e.IsOrdered);
        Assert.NotNull(ordered);
        Assert.Contains(ordered!.Chapters, c => !string.IsNullOrEmpty(c.SegmentUuid));
        _output.WriteLine($"Editions parsed: {info.Editions.Count}");
    }

    private static string? FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Jellyfin.sln")))
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
}
