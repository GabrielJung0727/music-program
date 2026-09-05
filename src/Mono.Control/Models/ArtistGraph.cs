using System.Text.Json.Serialization;

namespace Mono.Control.Models;

/// <summary>graph 응답. Core 로컬 그래프 쿼리이므로 클라우드 왕복이 없다.</summary>
public sealed class ArtistGraph
{
    [JsonPropertyName("artist")] public GraphArtist? Artist { get; set; }
    [JsonPropertyName("related")] public List<GraphArtist> Related { get; set; } = [];
    [JsonPropertyName("neighbours")] public List<GraphArtist> Neighbours { get; set; } = [];

    public bool HasRelated => Related.Count > 0;
    public bool HasNeighbours => Neighbours.Count > 0;
}

public sealed class GraphArtist
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("bio")] public string? Bio { get; set; }
}
