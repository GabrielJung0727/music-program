using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 스트리밍 서비스 어댑터 경계. 실제 Tidal/Qobuz SDK는 이 클래스 뒤에 붙이고,
/// 토큰은 Core에만 남는다(Control로 나가지 않는다).
/// </summary>
public sealed class StreamingHub
{
    private readonly Dictionary<StreamingProvider, StreamingAccount> _accounts = new();
    private readonly CatalogStore _catalog;
    private readonly object _gate = new();

    public StreamingHub(CatalogStore catalog) => _catalog = catalog;

    public IReadOnlyList<object> AccountViews
    {
        get
        {
            lock (_gate)
            {
                return _accounts.Values
                    .Select(a => (object)new { provider = a.Provider, connected = a.Connected, displayName = a.DisplayName })
                    .ToList();
            }
        }
    }

    public bool IsConnected(StreamingProvider provider)
    {
        if (provider == StreamingProvider.Local)
        {
            return true;
        }

        lock (_gate)
        {
            return _accounts.TryGetValue(provider, out var acc) && acc.Connected;
        }
    }

    public StreamingAccount Link(StreamingProvider provider, string token, string? displayName)
    {
        var acc = new StreamingAccount
        {
            Provider = provider,
            Token = token,
            Connected = !string.IsNullOrWhiteSpace(token),
            DisplayName = displayName ?? provider.ToString()
        };

        lock (_gate)
        {
            _accounts[provider] = acc;
        }

        if (acc.Connected)
        {
            SeedProvider(provider);
        }

        return acc;
    }

    public void Unlink(StreamingProvider provider)
    {
        lock (_gate)
        {
            _accounts.Remove(provider);
        }
    }

    /// <summary>어댑터 데모 카탈로그. 실제 SDK 연동 시 이 메서드만 교체한다.</summary>
    private void SeedProvider(StreamingProvider provider)
    {
        var (artistId, artistName, albumId, albumTitle, trackTitle, rate, depth, quality) = provider switch
        {
            StreamingProvider.Qobuz => ("ar-hiromi", "Hiromi", "al-spectrum", "Spectrum", "Spectrum", 192000, 24, StreamingQuality.Studio),
            _ => ("ar-brubeck", "Dave Brubeck", "al-time-out", "Time Out", "Take Five", 96000, 24, StreamingQuality.Max)
        };

        var artist = _catalog.Artists.GetValueOrDefault(artistId) ?? new Artist
        {
            Id = artistId,
            Name = artistName,
            RelatedArtistIds = ["ar-miles"],
            Bio = $"{provider} 어댑터 데모 카탈로그."
        };
        var album = _catalog.Albums.GetValueOrDefault(albumId) ?? new Album
        {
            Id = albumId,
            Title = albumTitle,
            ArtistId = artist.Id,
            Label = provider.ToString(),
            Year = 1959,
            LinerNotes = $"{provider} {quality} 어댑터로 연동된 데모 앨범. 실제 SDK는 이 어댑터 뒤에 붙는다.",
            Credits = artistName
        };

        var trackId = $"tr-{provider.ToString().ToLowerInvariant()}-{albumId[3..]}";
        var mergedWithLocal = _catalog.Tracks.Values.Any(t =>
            t.LocalPath is not null && t.Title.Contains(trackTitle, StringComparison.OrdinalIgnoreCase));
        var track = new Track
        {
            Id = trackId,
            Title = $"{trackTitle} ({provider})",
            AlbumId = album.Id,
            ArtistId = artist.Id,
            StreamingId = $"{provider}:{albumId}",
            Source = provider,
            StreamingQuality = quality,
            SampleRate = rate,
            BitDepth = depth,
            Channels = 2,
            DurationMs = 240000,
            MergedLocalAndStreaming = mergedWithLocal,
            LyricsLrc = "[00:00.00]스트리밍 소스 — 각자 자기 계정으로 재생\n[00:20.00]Core는 타임라인만 방송한다"
        };
        _catalog.UpsertTrack(track, album, artist);
    }
}
