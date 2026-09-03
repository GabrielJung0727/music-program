using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 스트리밍 서비스 어댑터. 토큰은 Core에만 보관.
/// env에 파트너 Client ID/Secret이 있으면 실 OAuth·카탈로그/스트림 URL 경로를 쓰고,
/// 없으면 데모 토큰·시드 카탈로그로 폴백한다.
/// </summary>
public sealed class StreamingHub
{
    private static readonly HttpClient Http = CreateHttp();

    private readonly Dictionary<StreamingProvider, StreamingAccount> _accounts = new();
    private readonly Dictionary<StreamingProvider, string> _pendingStates = new();
    private readonly Dictionary<string, string> _streamUrlCache = new(StringComparer.Ordinal);
    private readonly CatalogStore _catalog;
    private readonly object _gate = new();

    public StreamingHub(CatalogStore catalog) => _catalog = catalog;

    public bool HasLiveCredentials(StreamingProvider provider) => provider switch
    {
        StreamingProvider.Tidal => !string.IsNullOrWhiteSpace(Env("MONO_TIDAL_CLIENT_ID")),
        StreamingProvider.Qobuz => !string.IsNullOrWhiteSpace(Env("MONO_QOBUZ_APP_ID")),
        _ => false
    };

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
                        liveSdk = HasLiveCredentials(a.Provider),
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

    public object BeginOAuth(StreamingProvider provider)
    {
        var state = Guid.NewGuid().ToString("n")[..12];
        lock (_gate) _pendingStates[provider] = state;

        var redirect = Env("MONO_OAUTH_REDIRECT") ?? "http://127.0.0.1:7702/oauth/callback";
        var authUrl = provider switch
        {
            StreamingProvider.Tidal => BuildTidalAuthUrl(state, redirect),
            StreamingProvider.Qobuz => BuildQobuzAuthUrl(state, redirect),
            _ => ""
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(authUrl))
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch { /* headless / CI */ }

        var live = HasLiveCredentials(provider);
        return new
        {
            provider,
            state,
            authUrl,
            liveSdk = live,
            note = live
                ? "실 파트너 자격증명이 설정됨. 콜백 code로 CompleteOAuth하세요."
                : "파트너 SDK 승인 후 MONO_TIDAL_CLIENT_ID / MONO_QOBUZ_APP_ID 등을 설정하세요. 지금은 데모 토큰으로 CompleteOAuth 가능."
        };
    }

    public StreamingAccount CompleteOAuth(StreamingProvider provider, string? codeOrToken, string? state, string? displayName)
    {
        lock (_gate)
        {
            if (state is not null && _pendingStates.TryGetValue(provider, out var expected) && expected != state)
                throw new InvalidOperationException("OAuth state mismatch");
            _pendingStates.Remove(provider);
        }

        var token = codeOrToken;
        if (HasLiveCredentials(provider) && LooksLikeAuthCode(codeOrToken))
            token = ExchangeCodeForToken(provider, codeOrToken!) ?? codeOrToken;
        else if (string.IsNullOrWhiteSpace(token))
            token = $"demo-{provider}-{DateTimeOffset.UtcNow:yyyyMMdd}";

        return Link(provider, token!, displayName ?? provider.ToString());
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
        if (acc.Connected)
        {
            if (HasLiveCredentials(provider))
                _ = TryImportLiveCatalogAsync(provider, token);
            SeedProvider(provider);
        }
        return acc;
    }

    public void Unlink(StreamingProvider provider)
    {
        lock (_gate)
        {
            _accounts.Remove(provider);
            foreach (var key in _streamUrlCache.Keys.Where(k => k.StartsWith(provider + ":", StringComparison.Ordinal)).ToList())
                _streamUrlCache.Remove(key);
        }
    }

    /// <summary>Clock-sync Output이 열 수 있는 HTTP(S) 스트림 URL. 없으면 null.</summary>
    public string? ResolveStreamUrl(Track track)
    {
        if (track.Source is not (StreamingProvider.Tidal or StreamingProvider.Qobuz))
            return null;

        var cacheKey = $"{track.Source}:{track.StreamingId ?? track.Id}";
        lock (_gate)
        {
            if (_streamUrlCache.TryGetValue(cacheKey, out var hit))
                return hit;
            if (!_accounts.TryGetValue(track.Source, out var acc) || !acc.Connected)
                return null;
        }

        if (!HasLiveCredentials(track.Source))
            return null;

        try
        {
            var url = track.Source switch
            {
                StreamingProvider.Tidal => FetchTidalManifestUrl(track, GetToken(track.Source)),
                StreamingProvider.Qobuz => FetchQobuzFileUrl(track, GetToken(track.Source)),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(url))
            {
                lock (_gate) _streamUrlCache[cacheKey] = url!;
            }
            return url;
        }
        catch
        {
            return null;
        }
    }

    private string? GetToken(StreamingProvider provider)
    {
        lock (_gate) return _accounts.TryGetValue(provider, out var a) ? a.Token : null;
    }

    private static bool LooksLikeAuthCode(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.StartsWith("demo-", StringComparison.OrdinalIgnoreCase)
           && value.Length < 200;

    private string BuildTidalAuthUrl(string state, string redirect)
    {
        var clientId = Env("MONO_TIDAL_CLIENT_ID") ?? "MONO_TIDAL_CLIENT";
        return $"https://login.tidal.com/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}"
               + $"&redirect_uri={Uri.EscapeDataString(redirect)}&state={state}&scope=r_usr+w_usr";
    }

    private string BuildQobuzAuthUrl(string state, string redirect)
    {
        var appId = Env("MONO_QOBUZ_APP_ID") ?? "MONO_QOBUZ_CLIENT";
        return $"https://www.qobuz.com/login?state={state}&client_id={Uri.EscapeDataString(appId)}"
               + $"&redirect_uri={Uri.EscapeDataString(redirect)}";
    }

    private string? ExchangeCodeForToken(StreamingProvider provider, string code)
    {
        try
        {
            return provider switch
            {
                StreamingProvider.Tidal => ExchangeTidal(code),
                StreamingProvider.Qobuz => ExchangeQobuz(code),
                _ => null
            };
        }
        catch { return null; }
    }

    private static string? ExchangeTidal(string code)
    {
        var clientId = Env("MONO_TIDAL_CLIENT_ID");
        var secret = Env("MONO_TIDAL_CLIENT_SECRET");
        var redirect = Env("MONO_OAUTH_REDIRECT") ?? "http://127.0.0.1:7702/oauth/callback";
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            return null;

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.tidal.com/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirect
        });
        using var res = Http.Send(req);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(res.Content.ReadAsStream());
        return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }

    private static string? ExchangeQobuz(string code)
    {
        var appId = Env("MONO_QOBUZ_APP_ID");
        var secret = Env("MONO_QOBUZ_APP_SECRET");
        if (string.IsNullOrWhiteSpace(appId)) return null;

        var url = $"https://www.qobuz.com/api.json/0.2/user/login?app_id={Uri.EscapeDataString(appId)}"
                  + $"&code={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrWhiteSpace(secret))
            url += $"&app_secret={Uri.EscapeDataString(secret)}";
        using var res = Http.GetAsync(url).GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(res.Content.ReadAsStream());
        if (doc.RootElement.TryGetProperty("user_auth_token", out var t))
            return t.GetString();
        return doc.RootElement.TryGetProperty("token", out var t2) ? t2.GetString() : null;
    }

    private async Task TryImportLiveCatalogAsync(StreamingProvider provider, string token)
    {
        try
        {
            if (provider == StreamingProvider.Tidal)
                await ImportTidalFavoritesAsync(token).ConfigureAwait(false);
            else if (provider == StreamingProvider.Qobuz)
                await ImportQobuzFavoritesAsync(token).ConfigureAwait(false);
        }
        catch { /* keep demo seed */ }
    }

    private async Task ImportTidalFavoritesAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.tidal.com/v1/users/me/favorites/tracks?limit=20&countryCode=US");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await Http.SendAsync(req).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("items", out var items)) return;
        var n = 0;
        foreach (var item in items.EnumerateArray())
        {
            var track = item.TryGetProperty("item", out var inner) ? inner : item;
            if (!track.TryGetProperty("id", out var idEl)) continue;
            var sid = idEl.ToString();
            var title = track.TryGetProperty("title", out var t) ? t.GetString() ?? "Track" : "Track";
            var artistName = "TIDAL";
            if (track.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0)
                artistName = artists[0].TryGetProperty("name", out var an) ? an.GetString() ?? artistName : artistName;
            UpsertStreamingTrack(StreamingProvider.Tidal, "tr-tidal-" + sid, sid, title, artistName, "TIDAL", 96000, 24, StreamingQuality.Max);
            if (++n >= 12) break;
        }
    }

    private async Task ImportQobuzFavoritesAsync(string token)
    {
        var appId = Env("MONO_QOBUZ_APP_ID");
        if (string.IsNullOrWhiteSpace(appId)) return;
        var url = $"https://www.qobuz.com/api.json/0.2/favorite/getUserFavorites?app_id={Uri.EscapeDataString(appId)}"
                  + $"&user_auth_token={Uri.EscapeDataString(token)}&type=tracks&limit=20";
        using var res = await Http.GetAsync(url).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("tracks", out var tracksObj)) return;
        if (!tracksObj.TryGetProperty("items", out var items)) return;
        var n = 0;
        foreach (var track in items.EnumerateArray())
        {
            if (!track.TryGetProperty("id", out var idEl)) continue;
            var sid = idEl.ToString();
            var title = track.TryGetProperty("title", out var t) ? t.GetString() ?? "Track" : "Track";
            var artistName = track.TryGetProperty("performer", out var p) && p.TryGetProperty("name", out var pn)
                ? pn.GetString() ?? "Qobuz"
                : "Qobuz";
            UpsertStreamingTrack(StreamingProvider.Qobuz, "tr-qobuz-" + sid, sid, title, artistName, "Qobuz", 192000, 24, StreamingQuality.Studio);
            if (++n >= 12) break;
        }
    }

    private void UpsertStreamingTrack(
        StreamingProvider provider, string trackId, string streamingId, string title, string artistName,
        string albumTitle, int rate, int depth, StreamingQuality quality)
    {
        var artistId = "ar-stream-" + Sanitize(artistName);
        var albumId = "al-stream-" + Sanitize(albumTitle) + "-" + provider;
        var artist = _catalog.Artists.GetValueOrDefault(artistId) ?? new Artist
        {
            Id = artistId,
            Name = artistName,
            Bio = $"{provider} live catalog"
        };
        var album = _catalog.Albums.GetValueOrDefault(albumId) ?? new Album
        {
            Id = albumId,
            Title = albumTitle,
            ArtistId = artist.Id,
            Label = provider.ToString(),
            LinerNotes = $"{provider} {quality} (live API)."
        };
        _catalog.UpsertTrack(new Track
        {
            Id = trackId,
            Title = title,
            AlbumId = album.Id,
            ArtistId = artist.Id,
            SampleRate = rate,
            BitDepth = depth,
            Channels = 2,
            DurationMs = 180000,
            Source = provider,
            StreamingId = streamingId,
            StreamingQuality = quality
        }, album, artist);
    }

    private string? FetchTidalManifestUrl(Track track, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var id = track.StreamingId ?? track.Id.Replace("tr-tidal-", "", StringComparison.OrdinalIgnoreCase);
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.tidal.com/v1/tracks/{Uri.EscapeDataString(id)}/playbackinfopostpaywall?playbackmode=STREAM&assetpresentation=FULL&audioquality=HI_RES");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = Http.Send(req);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(res.Content.ReadAsStream());
        if (!doc.RootElement.TryGetProperty("manifest", out var man)) return null;
        var raw = man.GetString();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            using var mdoc = JsonDocument.Parse(json);
            if (mdoc.RootElement.TryGetProperty("urls", out var urls) && urls.GetArrayLength() > 0)
                return urls[0].GetString();
        }
        catch
        {
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return raw;
        }
        return null;
    }

    private string? FetchQobuzFileUrl(Track track, string? token)
    {
        var appId = Env("MONO_QOBUZ_APP_ID");
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(token)) return null;
        var id = track.StreamingId ?? track.Id.Replace("tr-qobuz-", "", StringComparison.OrdinalIgnoreCase);
        var url = $"https://www.qobuz.com/api.json/0.2/track/getFileUrl?app_id={Uri.EscapeDataString(appId)}"
                  + $"&user_auth_token={Uri.EscapeDataString(token)}&track_id={Uri.EscapeDataString(id)}&format_id=27";
        using var res = Http.GetAsync(url).GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(res.Content.ReadAsStream());
        return doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
    }

    private void SeedProvider(StreamingProvider provider)
    {
        var (trackId, artistId, artistName, albumId, albumTitle, trackTitle, rate, depth, quality, streamingId) = provider switch
        {
            StreamingProvider.Qobuz => ("tr-qobuz-spectrum", "ar-hiromi", "Hiromi", "al-spectrum", "Spectrum", "Spectrum", 192000, 24, StreamingQuality.Studio, "spectrum-demo"),
            _ => ("tr-tidal-time-out", "ar-brubeck", "Dave Brubeck", "al-time-out", "Time Out", "Take Five", 96000, 24, StreamingQuality.Max, "take-five-demo")
        };

        var artist = _catalog.Artists.GetValueOrDefault(artistId) ?? new Artist
        {
            Id = artistId,
            Name = artistName,
            RelatedArtistIds = ["ar-miles"],
            Bio = HasLiveCredentials(provider)
                ? $"{provider} 어댑터 (live credentials)."
                : $"{provider} 어댑터 카탈로그 (OAuth 연동 후 실 SDK로 교체)."
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
            StreamingId = streamingId,
            StreamingQuality = quality
        }, album, artist);
    }

    private static string Sanitize(string s)
        => new(s.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());

    private static string? Env(string key)
    {
        var v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mono/0.1 (+https://github.com/GabrielJung0727/music-program)");
        return c;
    }
}
