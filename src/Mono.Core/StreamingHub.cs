using System.Diagnostics;
using System.Text.Json;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 스트리밍 서비스 어댑터. 토큰은 Core에만 보관.
/// OAuth: 브라우저로 인증 URL을 연 뒤 콜백 토큰을 Link로 전달하는 흐름을 지원한다.
/// </summary>
public sealed class StreamingHub
{
    private readonly Dictionary<StreamingProvider, StreamingAccount> _accounts = new();
    private readonly Dictionary<StreamingProvider, string> _pendingStates = new();
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
                    .Select(a => (object)new
                    {
                        provider = a.Provider,
                        connected = a.Connected,
                        displayName = a.DisplayName,
                        authMode = a.Provider == StreamingProvider.Tidal ? "oauth_browser" : "password_or_token"
                    })
                    .ToList();
            }
        }
    }

    public bool IsConnected(StreamingProvider provider)
    {
        if (provider == StreamingProvider.Local) return true;
        lock (_gate) return _accounts.TryGetValue(provider, out var acc) && acc.Connected;
    }

    /// <summary>OAuth 시작. Control에 authUrl을 돌려주고, 데모 환경에서는 브라우저를 연다.</summary>
    public object BeginOAuth(StreamingProvider provider)
    {
        var state = Guid.NewGuid().ToString("n")[..12];
        lock (_gate) _pendingStates[provider] = state;

        // 실제 파트너 Client ID가 오면 이 URL만 교체한다.
        var authUrl = provider switch
        {
            StreamingProvider.Tidal =>
                $"https://login.tidal.com/authorize?response_type=code&client_id=MONO_TIDAL_CLIENT&state={state}&scope=r_usr+w_usr",
            StreamingProvider.Qobuz =>
                $"https://www.qobuz.com/login?state={state}&client_id=MONO_QOBUZ_CLIENT",
            _ => ""
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(authUrl))
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch { /* headless / CI */ }

        return new { provider, state, authUrl, note = "파트너 SDK 승인 후 client_id를 교체하세요. 지금은 데모 토큰으로 CompleteOAuth 가능." };
    }

    public StreamingAccount CompleteOAuth(StreamingProvider provider, string? codeOrToken, string? state, string? displayName)
    {
        lock (_gate)
        {
            if (state is not null && _pendingStates.TryGetValue(provider, out var expected) && expected != state)
                throw new InvalidOperationException("OAuth state mismatch");
            _pendingStates.Remove(provider);
        }

        // 데모: code가 없으면 demo-token. 실연동 시 code→token 교환.
        var token = string.IsNullOrWhiteSpace(codeOrToken) ? $"demo-{provider}-{DateTimeOffset.UtcNow:yyyyMMdd}" : codeOrToken;
        return Link(provider, token, displayName ?? provider.ToString());
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

        lock (_gate) _accounts[provider] = acc;
        if (acc.Connected) SeedProvider(provider);
        return acc;
    }

    public void Unlink(StreamingProvider provider)
    {
        lock (_gate) _accounts.Remove(provider);
    }

    private void SeedProvider(StreamingProvider provider)
    {
        // 테스트·데모 호환 ID: tr-tidal-time-out / tr-qobuz-spectrum
        var (trackId, artistId, artistName, albumId, albumTitle, trackTitle, rate, depth, quality) = provider switch
        {
            StreamingProvider.Qobuz => ("tr-qobuz-spectrum", "ar-hiromi", "Hiromi", "al-spectrum", "Spectrum", "Spectrum", 192000, 24, StreamingQuality.Studio),
            _ => ("tr-tidal-time-out", "ar-brubeck", "Dave Brubeck", "al-time-out", "Time Out", "Take Five", 96000, 24, StreamingQuality.Max)
        };

        var artist = _catalog.Artists.GetValueOrDefault(artistId) ?? new Artist
        {
            Id = artistId,
            Name = artistName,
            RelatedArtistIds = ["ar-miles"],
            Bio = $"{provider} 어댑터 카탈로그 (OAuth 연동 후 실 SDK로 교체)."
        };
        var album = _catalog.Albums.GetValueOrDefault(albumId) ?? new Album
        {
            Id = albumId,
            Title = albumTitle,
            ArtistId = artist.Id,
            Label = provider.ToString(),
            Year = 1959,
            LinerNotes = $"{provider} {quality}. Official partner stream path (Clock-sync).",
            Credits = artistName
        };

        _catalog.UpsertTrack(new Track
        {
            Id = trackId,
            Title = trackTitle,
            AlbumId = album.Id,
            ArtistId = artist.Id,
            SampleRate = rate,
            BitDepth = depth,
            Channels = 2,
            DurationMs = 180000,
            TrackNumber = 1,
            Source = provider,
            StreamingQuality = quality
        }, album, artist);
    }
}
