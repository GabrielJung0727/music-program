using System.Text.Json.Serialization;

namespace Mono.Control.Models;

/// <summary>청음 히스토리 한 줄. Core 의 history 응답 형태 그대로다.</summary>
public sealed class HistoryEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("trackId")] public string TrackId { get; set; } = "";
    [JsonPropertyName("roomName")] public string? RoomName { get; set; }
    [JsonPropertyName("heardAt")] public DateTimeOffset HeardAt { get; set; }
    [JsonPropertyName("completed")] public bool Completed { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string? Artist { get; set; }

    public string When => HeardAt.ToLocalTime().ToString("M월 d일 HH:mm");

    public string Detail => string.Join(" · ", new[]
    {
        Artist,
        string.IsNullOrWhiteSpace(RoomName) ? null : RoomName,
        Completed ? "완청" : null
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>플레이리스트 한 건.</summary>
public sealed class PlaylistEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("fromArchiveId")] public string? FromArchiveId { get; set; }
    [JsonPropertyName("tracks")] public List<PlaylistTrack> Tracks { get; set; } = [];

    public string Subtitle => $"{Tracks.Count}곡 · {CreatedAt.ToLocalTime():yyyy-MM-dd}"
                              + (string.IsNullOrEmpty(FromArchiveId) ? "" : " · 세션 아카이브");
}

public sealed class PlaylistTrack
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
}
