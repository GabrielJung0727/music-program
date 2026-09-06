using System.Text.Json.Serialization;

namespace Mono.Control.Models;

/// <summary>앨범 상세 오버레이가 보여 주는 내용. catalog 에 없는 라이너·크레딧이 여기 있다.</summary>
public sealed class AlbumDetail
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string? Artist { get; set; }
    [JsonPropertyName("year")] public int? Year { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("linerNotes")] public string? LinerNotes { get; set; }
    [JsonPropertyName("credits")] public string? Credits { get; set; }
    [JsonPropertyName("tracks")] public List<CatalogTrack> Tracks { get; set; } = [];

    public string Subtitle => string.Join(" · ", new[]
    {
        Artist,
        Year?.ToString(),
        Label,
        $"{Tracks.Count}곡"
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool HasLinerNotes => !string.IsNullOrWhiteSpace(LinerNotes);
    public bool HasCredits => !string.IsNullOrWhiteSpace(Credits);
}
