using System.Text.RegularExpressions;

namespace Mono.Core;

/// <summary>작품 식별자. Key는 그룹핑용, Title은 화면 표시용이다.</summary>
public readonly record struct WorkId(string Key, string Title);

/// <summary>
/// 같은 작품(Work)의 여러 악장·연주를 묶는다.
/// 클래식 표기 "작품명: I. 악장"에서 악장 꼬리만 떼되, 악장으로 보이지 않으면 건드리지 않는다.
/// 작곡가를 모르면 아예 묶지 않는다 — 제목만으로 묶으면 서로 다른 곡이 합쳐진다.
/// </summary>
public static partial class CompositionGrouping
{
    /// <summary>"I." "IV." "1." 처럼 로마 숫자나 아라비아 숫자로 시작하는 꼬리만 악장으로 본다.</summary>
    [GeneratedRegex(@"^\s*(?:[IVXLC]+|\d{1,2})\s*[\.\)]\s+\S", RegexOptions.IgnoreCase)]
    private static partial Regex MovementHead();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static WorkId? Identify(string trackTitle, string? composer)
    {
        if (string.IsNullOrWhiteSpace(trackTitle)) return null;
        if (string.IsNullOrWhiteSpace(composer)) return null;

        var title = trackTitle.Trim();
        var cut = title.LastIndexOf(':');
        if (cut > 0 && cut < title.Length - 1)
        {
            var tail = title[(cut + 1)..];
            if (MovementHead().IsMatch(tail))
            {
                title = title[..cut].TrimEnd();
            }
        }

        var key = Normalize(composer) + "|" + Normalize(title);
        return new WorkId(key, title);
    }

    private static string Normalize(string s)
        => Whitespace().Replace(s.Trim(), " ").ToLowerInvariant();
}
