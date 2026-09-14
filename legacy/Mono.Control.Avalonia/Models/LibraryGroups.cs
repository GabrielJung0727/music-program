using System.Text.Json.Serialization;

namespace Mono.Control.Models;

/// <summary>Genres 타일 한 장. 하드코딩이 아니라 실제 태그 집계다.</summary>
public sealed record GenreCount(string Name, int TrackCount)
{
    public string Subtitle => $"{TrackCount}곡";
}

/// <summary>Composers 아바타 한 장. 사진이 없으면 이니셜을 쓴다.</summary>
public sealed record ComposerEntry(string Name, int TrackCount)
{
    /// <summary>라틴 이름은 머리글자 두 개, 그 외(한중일 등)는 첫 글자 하나.</summary>
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1 || !char.IsAscii(parts[0][0])) return parts[0][..1];
            return $"{parts[0][0]}{parts[^1][0]}";
        }
    }

    public string Subtitle => $"{TrackCount}곡";
}

/// <summary>Compositions 표의 한 줄. 같은 작품의 여러 연주를 묶는다.</summary>
public sealed record CompositionEntry(string WorkKey, string Title, string Composer, int PerformanceCount)
{
    public string PerformanceText => PerformanceCount == 1 ? "연주 1건" : $"연주 {PerformanceCount}건";
}

/// <summary>Folders 목록의 한 줄. Core 의 folders 응답을 그대로 받는다.</summary>
public sealed class FolderEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("exists")] public bool Exists { get; set; }
    [JsonPropertyName("trackCount")] public int TrackCount { get; set; }

    public string Status => Exists ? $"{TrackCount}곡" : "폴더를 찾을 수 없음";
}
