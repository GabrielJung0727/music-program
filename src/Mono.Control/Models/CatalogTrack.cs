using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mono.Control.Models;

public partial class CatalogTrack : ObservableObject
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
    [JsonPropertyName("source")] public int Source { get; set; }
    [JsonPropertyName("mergedLocalAndStreaming")] public bool MergedLocalAndStreaming { get; set; }
    [JsonPropertyName("hasLyrics")] public bool HasLyrics { get; set; }

    [ObservableProperty] private Bitmap? _cover;

    partial void OnCoverChanged(Bitmap? value) => OnPropertyChanged(nameof(HasArt));

    public string Subtitle => string.Join(" · ", new[] { Artist, Album }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string DurationText => TimeSpan.FromMilliseconds(DurationMs).ToString(@"m\:ss");
    public string AbsoluteArtUrl => string.IsNullOrWhiteSpace(ArtUrl) ? "" : "http://127.0.0.1:7702" + ArtUrl;
    public bool HasArt => Cover is not null;
    public string GenreHint
    {
        get
        {
            if (IsDsd) return "DSD";
            if (SampleRate >= 96000) return "Hi-Res";
            if (Source == 1) return "Tidal";
            if (Source == 2) return "Qobuz";
            if (MergedLocalAndStreaming) return "Local+Stream";
            return "Library";
        }
    }
}

public sealed class RoomListItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("mode")] public int Mode { get; set; }
}

public sealed class NavItem
{
    public NavItem(string id, string label, string section, string icon)
    {
        Id = id;
        Label = label;
        Section = section;
        Icon = icon;
    }

    public string Id { get; }
    public string Label { get; }
    public string Section { get; }
    public string Icon { get; }
}

public sealed class GenreTile
{
    public GenreTile(string id, string label, string accent, string subtitle, string motif)
    {
        Id = id;
        Label = label;
        Accent = accent;
        Subtitle = subtitle;
        Motif = motif;
    }

    public string Id { get; }
    public string Label { get; }
    public string Accent { get; }
    public string Subtitle { get; }
    public string Motif { get; }
}

public sealed class HomeRail
{
    public HomeRail(string title, IEnumerable<CatalogTrack> items)
    {
        Title = title;
        Items = new(items);
    }

    public string Title { get; }
    public System.Collections.ObjectModel.ObservableCollection<CatalogTrack> Items { get; }
}
