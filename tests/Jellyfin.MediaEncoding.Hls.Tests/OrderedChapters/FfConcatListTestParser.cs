using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jellyfin.MediaEncoding.Hls.Tests.OrderedChapters;

/// <summary>
/// Parses ffconcat version 1.0 files produced by <see cref="MediaBrowser.Controller.MediaEncoding.OrderedChapterTranscodePlanner.WriteConcatDemuxerFile"/>
/// for test assertions. <c>inpoint</c>/<c>outpoint</c> may be sexagesimal (<c>MM:SS.ffffff</c>) or legacy integer microseconds.
/// </summary>
internal static class FfConcatListTestParser
{
    /// <summary>
    /// Parses segments in order. Throws if the structure is invalid.
    /// </summary>
    public static IReadOnlyList<(string Path, long InMicroseconds, long OutMicroseconds)> Parse(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<(string, long, long)>();
        var i = 0;
        if (i < lines.Length && lines[i].StartsWith("ffconcat", StringComparison.Ordinal))
        {
            i++;
        }

        while (i < lines.Length)
        {
            if (i + 2 >= lines.Length)
            {
                throw new InvalidOperationException("ffconcat: truncated after line " + i);
            }

            var path = ParseFileLine(lines[i]);
            if (!TryParseTimeLine(lines[i + 1], "inpoint", out var inUs))
            {
                throw new InvalidOperationException("ffconcat: expected inpoint: " + lines[i + 1]);
            }

            if (!TryParseTimeLine(lines[i + 2], "outpoint", out var outUs))
            {
                throw new InvalidOperationException("ffconcat: expected outpoint: " + lines[i + 2]);
            }

            if (inUs >= outUs)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "ffconcat: inpoint {0} >= outpoint {1} for {2}", inUs, outUs, path));
            }

            list.Add((path, inUs, outUs));
            i += 3;
        }

        return list;
    }

    private static bool TryParseTimeLine(string line, string keyword, out long microseconds)
    {
        microseconds = 0;
        var prefix = keyword + " ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var value = line.AsSpan(prefix.Length).Trim();
        if (value.IndexOf(':') < 0)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out microseconds);
        }

        var s = value.ToString();
        var parts = s.Split(':');
        double totalSeconds;
        if (parts.Length == 2)
        {
            totalSeconds = (double.Parse(parts[0], CultureInfo.InvariantCulture) * 60.0)
                + double.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        else if (parts.Length == 3)
        {
            totalSeconds = (double.Parse(parts[0], CultureInfo.InvariantCulture) * 3600.0)
                + (double.Parse(parts[1], CultureInfo.InvariantCulture) * 60.0)
                + double.Parse(parts[2], CultureInfo.InvariantCulture);
        }
        else
        {
            return false;
        }

        microseconds = (long)Math.Round(totalSeconds * 1_000_000.0, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>
    /// Unescapes paths written with <c>seg.Path.Replace("'", @"'\''")</c>.
    /// </summary>
    private static string ParseFileLine(string line)
    {
        const string prefix = "file ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ffconcat: expected file line: " + line);
        }

        var s = line.AsSpan(prefix.Length).TrimStart();
        if (s.Length < 2 || s[0] != '\'')
        {
            throw new InvalidOperationException("ffconcat: file path must be single-quoted: " + line);
        }

        var sb = new StringBuilder();
        var j = 1;
        while (j < s.Length)
        {
            // OrderedChapterTranscodePlanner: path.Replace("'", @"'\''") → '\'' inside the outer '...'
            if (j + 3 < s.Length
                && s[j] == '\''
                && s[j + 1] == '\\'
                && s[j + 2] == '\''
                && s[j + 3] == '\'')
            {
                sb.Append('\'');
                j += 4;
                continue;
            }

            if (s[j] == '\'')
            {
                break;
            }

            sb.Append(s[j]);
            j++;
        }

        if (j >= s.Length || s[j] != '\'')
        {
            throw new InvalidOperationException("ffconcat: missing closing quote: " + line);
        }

        return sb.ToString();
    }
}
