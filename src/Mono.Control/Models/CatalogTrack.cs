using System.Text.Json.Serialization;

namespace Mono.Control.Models;

public sealed class CatalogTrack
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string? Artist { get; set; }
    [JsonPropertyName("album")] public string? Album { get; set; }
    [JsonPropertyName("artistId")] public string? ArtistId { get; set; }
    [JsonPropertyName("albumId")] public string? AlbumId { get; set; }
    [JsonPropertyName("durationMs")] public long DurationMs { get; set; }
    [JsonPropertyName("badge")] public string? Badge { get; set; }
    [JsonPropertyName("artUrl")] public string? ArtUrl { get; set; }
    [JsonPropertyName("year")] public int? Year { get; set; }
    [JsonPropertyName("sampleRate")] public int SampleRate { get; set; }
    [JsonPropertyName("bitDepth")] public int BitDepth { get; set; }
    [JsonPropertyName("isDsd")] public bool IsDsd { get; set; }
    [JsonPropertyName("mergedLocalAndStreaming")] public bool MergedLocalAndStreaming { get; set; }

    public string Subtitle => string.Join(" · ", new[] { Artist, Album }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string DurationText => TimeSpan.FromMilliseconds(DurationMs).ToString(@"m\:ss");
    public string AbsoluteArtUrl => string.IsNullOrWhiteSpace(ArtUrl) ? "" : "http://127.0.0.1:7702" + ArtUrl;
}

public sealed class RoomListItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("mode")] public int Mode { get; set; }
}

public sealed class NavItem
{
    public NavItem(string id, string label, string section)
    {
        Id = id;
        Label = label;
        Section = section;
    }

    public string Id { get; }
    public string Label { get; }
    public string Section { get; }
}
