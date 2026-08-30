using System.Globalization;
using System.Text.RegularExpressions;
using Auralis.Shared;

namespace Auralis.Protocol;

public static class LyricsParser
{
    private static readonly Regex LineRx = new(@"\[(\d+):(\d+(?:\.\d+)?)\](.*)", RegexOptions.Compiled);

    public static IReadOnlyList<LyricsLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return [];
        }

        var lines = new List<LyricsLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var m = LineRx.Match(raw.Trim());
            if (!m.Success)
            {
                continue;
            }

            var min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var sec = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var ms = (long)((min * 60 + sec) * 1000);
            lines.Add(new LyricsLine(ms, m.Groups[3].Value.Trim()));
        }

        return lines.OrderBy(l => l.TimeMs).ToList();
    }

    public static LyricsLine? At(IReadOnlyList<LyricsLine> lines, long mediaTimeMs)
    {
        LyricsLine? current = null;
        foreach (var line in lines)
        {
            if (line.TimeMs <= mediaTimeMs)
            {
                current = line;
            }
            else
            {
                break;
            }
        }

        return current;
    }
}
