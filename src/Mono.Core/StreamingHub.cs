using System.Diagnostics;
using System.Text.Json;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 스트리밍 서비스 어댑터. 토큰은 Core에만 보관.
/// Tidal은 임베디드 QA Client ID + PKCE로 실 OAuth를 타고,
/// Qobuz는 env 키가 있을 때만 실경로, 없으면 데모로 폴백한다.
/// </summary>
public sealed class StreamingHub
{
    private static readonly HttpClient Http = CreateHttp();
    private static readonly TidalClient Tidal = new(Http);

    private readonly Dictionary<StreamingProvider, StreamingAccount> _accounts = new();
    private readonly Dictionary<StreamingProvider, PendingAuth> _pending = new();
    private readonly Dictionary<string, string> _streamUrlCache = new(StringComparer.Ordinal);
    private readonly CatalogStore _catalog;
    private readonly string? _storePath;
    private readonly object _gate = new();
    private string _tidalCountry = "US";

    public StreamingHub(CatalogStore catalog, string? storePath = null)
    {
        _catalog = catalog;
        _storePath = storePath;
        Load();
    }

    public bool HasLiveCredentials(StreamingProvider provider) => provider switch
    {
        StreamingProvider.Tidal => !TidalClient.DemoForced && !string.IsNullOrWhiteSpace(TidalClient.ClientId),
        StreamingProvider.Qobuz => !string.IsNullOrWhiteSpace(Env("MONO_QOBUZ_APP_ID")),
        StreamingProvider.Local => false,
        _ => throw Unexpected(provider)
    };

    public string? LastImportNote { get; private set; }

    public int LiveTrackCount()
    {
        return _catalog.Tracks.Values.Count(t =>
            t.Source == StreamingProvider.Tidal &&
            !string.Equals(t.StreamingId, "take-five-demo", StringComparison.Ordinal));
    }

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
                        imported = a.Provider == StreamingProvider.Tidal ? LiveTrackCount() : 0,
                        note = a.Provider == StreamingProvider.Tidal ? LastImportNote : null,
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
        if (provider == StreamingProvider.Tidal && IsConnected(provider) && EnsureFreshTidalToken())
        {
            return new
            {
                provider,
                state = "",
                authUrl = "",
                liveSdk = true,
                demo = false,
                already = true,
                connected = true,
                displayName = DisplayName(provider),
                note = "이미 Tidal에 연동되어 있습니다."
            };
        }

        var state = Guid.NewGuid().ToString("n")[..12];
        string verifier = "";
        string challenge = "";
        if (provider == StreamingProvider.Tidal)
        {
            var pkce = TidalClient.CreatePkce();
            verifier = pkce.Verifier;
            challenge = pkce.Challenge;
        }
        lock (_gate) _pending[provider] = new PendingAuth(state, verifier);

        if (!HasLiveCredentials(provider))
        {
            var acc = CompleteOAuth(provider, null, state, displayName: null);
            return new
            {
                provider,
                state,
                authUrl = "",
                liveSdk = false,
                demo = true,
                connected = acc.Connected,
                displayName = acc.DisplayName,
                note = "파트너 키 없음 — 데모 토큰으로 연동했습니다."
            };
        }

        var authUrl = provider switch
        {
            StreamingProvider.Tidal => Tidal.BuildAuthorizeUrl(state, challenge),
            StreamingProvider.Qobuz => BuildQobuzAuthUrl(state),
            StreamingProvider.Local => "",
            _ => throw Unexpected(provider)
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(authUrl) && !InTestHost())
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch { /* headless / CI */ }

        return new
        {
            provider,
            state,
            authUrl,
            liveSdk = true,
            demo = false,
            connected = false,
            note = "브라우저에서 로그인하면 Core가 콜백을 받아 연동을 마칩니다."
        };
    }

    public StreamingAccount CompleteOAuth(StreamingProvider provider, string? codeOrToken, string? state, string? displayName)
    {
        PendingAuth? pending = null;
        lock (_gate)
        {
            if (state is not null && _pending.TryGetValue(provider, out var expected) && expected.State != state)
                throw new InvalidOperationException("OAuth state mismatch");
            if (_pending.TryGetValue(provider, out var p))
                pending = p;
            _pending.Remove(provider);
        }

        var token = codeOrToken;
        string? refresh = null;
        DateTimeOffset? expires = null;
        if (HasLiveCredentials(provider) && LooksLikeAuthCode(codeOrToken))
        {
            if (provider == StreamingProvider.Tidal)
            {
                var tokens = Tidal.ExchangeCode(codeOrToken!, pending?.Verifier ?? "");
                if (tokens is null)
                    throw new InvalidOperationException("Tidal 토큰 교환에 실패했습니다. Client ID/Secret과 Redirect URI를 확인하세요.");
                token = tokens.AccessToken;
                refresh = tokens.RefreshToken;
                expires = tokens.ExpiresAt;
            }
            else
            {
                token = ExchangeQobuz(codeOrToken!) ?? codeOrToken;
            }
        }
        else if (string.IsNullOrWhiteSpace(token))
        {
            token = $"demo-{provider}-{DateTimeOffset.UtcNow:yyyyMMdd}";
        }

        var name = displayName;
        TidalProfile? profile = null;
        if (provider == StreamingProvider.Tidal && HasLiveCredentials(provider) && LooksLikeLiveAccessToken(token))
        {
            profile = Tidal.FetchProfile(token!);
            name ??= profile.DisplayName;
            _tidalCountry = profile.Country;
        }

        return Link(provider, token!, name ?? provider.ToString(), refresh, expires, profile);
    }

    public StreamingAccount Link(StreamingProvider provider, string token, string? displayName)
        => Link(provider, token, displayName, refreshToken: null, expiresAt: null, profile: null);

    private StreamingAccount Link(
        StreamingProvider provider, string token, string? displayName, string? refreshToken, DateTimeOffset? expiresAt, TidalProfile? profile)
    {
        var acc = new StreamingAccount
        {
            Provider = provider,
            Token = token,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            Connected = !string.IsNullOrWhiteSpace(token),
            DisplayName = displayName ?? provider.ToString()
        };

        lock (_gate) _accounts[provider] = acc;
        if (acc.Connected)
        {
            var live = HasLiveCredentials(provider) && LooksLikeLiveAccessToken(token);
            if (live)
                ImportLiveNow(provider, token, profile);
            else
                SeedProvider(provider);
            Save();
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
        Save();
    }

    public void EnrichSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || !IsConnected(StreamingProvider.Tidal) || !HasLiveCredentials(StreamingProvider.Tidal))
            return;
        if (!EnsureFreshTidalToken()) return;
        var token = GetToken(StreamingProvider.Tidal);
        if (string.IsNullOrWhiteSpace(token) || LooksDemo(token)) return;
        try
        {
            foreach (var hit in Tidal.Search(token, query, _tidalCountry, 20))
                UpsertTidalHit(hit);
        }
        catch { /* local search still works */ }
    }

    /// <summary>Clock-sync Output이 열 수 있는 HTTP(S) 스트림 URL 또는 로컬 경로. 없으면 null.</summary>
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
                StreamingProvider.Tidal => FetchTidalUrl(track),
                StreamingProvider.Qobuz => FetchQobuzFileUrl(track, GetToken(track.Source)),
                StreamingProvider.Local => null,
                _ => throw Unexpected(track.Source)
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

    public string? ResolvePlayablePath(Track track)
        => track.LocalPath ?? ResolveStreamUrl(track);

    private string? FetchTidalUrl(Track track)
    {
        if (!EnsureFreshTidalToken()) return null;
        var token = GetToken(StreamingProvider.Tidal);
        if (string.IsNullOrWhiteSpace(token)) return null;
        var id = track.StreamingId ?? track.Id.Replace("tr-tidal-", "", StringComparison.OrdinalIgnoreCase);
        return Tidal.FetchPlaybackUrl(token, id, _tidalCountry);
    }

    private bool EnsureFreshTidalToken()
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(StreamingProvider.Tidal, out var acc) || !acc.Connected)
                return false;
            if (LooksDemo(acc.Token)) return false;
            if (acc.ExpiresAt is null || acc.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                return true;
            if (string.IsNullOrWhiteSpace(acc.RefreshToken)) return false;
            var next = Tidal.Refresh(acc.RefreshToken);
            if (next is null) return false;
            acc.Token = next.AccessToken;
            acc.RefreshToken = next.RefreshToken ?? acc.RefreshToken;
            acc.ExpiresAt = next.ExpiresAt;
        }
        Save();
        return true;
    }

    private string? GetToken(StreamingProvider provider)
    {
        lock (_gate) return _accounts.TryGetValue(provider, out var a) ? a.Token : null;
    }

    private string? DisplayName(StreamingProvider provider)
    {
        lock (_gate) return _accounts.TryGetValue(provider, out var a) ? a.DisplayName : provider.ToString();
    }

    private static bool LooksLikeAuthCode(string? value)
        => !string.IsNullOrWhiteSpace(value) && !LooksDemo(value);

    private static bool LooksDemo(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith("demo-", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeLiveAccessToken(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !LooksDemo(value)
           && value.Count(c => c == '.') == 2
           && value.Length > 40;

    private void ImportLiveNow(StreamingProvider provider, string token, TidalProfile? profile)
    {
        try
        {
            if (provider == StreamingProvider.Tidal)
            {
                if (!EnsureFreshTidalToken()) return;
                var access = GetToken(StreamingProvider.Tidal) ?? token;
                ImportTidal(access, profile);
            }
            else if (provider == StreamingProvider.Qobuz)
                ImportQobuzFavoritesAsync(token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LastImportNote = ex.Message;
        }
    }

    private void ImportTidal(string token, TidalProfile? profile)
    {
        profile ??= Tidal.FetchProfile(token);
        _tidalCountry = profile.Country;
        LastImportNote = null;
        var before = LiveTrackCount();
        foreach (var hit in Tidal.FetchFavorites(token, profile, 40))
            UpsertTidalHit(hit);
        foreach (var seed in new[] { "Miles Davis", "Hiromi", "Kind of Blue", "Daft Punk" })
        {
            foreach (var hit in Tidal.Search(token, seed, _tidalCountry, 12))
                UpsertTidalHit(hit);
        }

        var added = LiveTrackCount() - before;
        LastImportNote = added > 0
            ? $"{added}곡을 Tidal에서 가져왔습니다."
            : (Tidal.LastError ?? "Tidal API가 곡을 돌려주지 않았습니다.");
    }

    private static string BuildQobuzAuthUrl(string state)
    {
        var appId = Env("MONO_QOBUZ_APP_ID") ?? "MONO_QOBUZ_CLIENT";
        var redirect = Env("MONO_OAUTH_REDIRECT") ?? TidalClient.DefaultRedirect;
        return $"https://www.qobuz.com/login?state={state}&client_id={Uri.EscapeDataString(appId)}"
               + $"&redirect_uri={Uri.EscapeDataString(redirect)}";
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

    private void UpsertTidalHit(TidalTrackHit hit)
        => UpsertStreamingTrack(
            StreamingProvider.Tidal,
            "tr-tidal-" + hit.Id,
            hit.Id,
            hit.Title,
            hit.Artist,
            hit.Album,
            96000,
            24,
            StreamingQuality.Max,
            hit.DurationMs);

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
            UpsertStreamingTrack(StreamingProvider.Qobuz, "tr-qobuz-" + sid, sid, title, artistName, "Qobuz", 192000, 24, StreamingQuality.Studio, 180000);
            if (++n >= 12) break;
        }
    }

    private void UpsertStreamingTrack(
        StreamingProvider provider, string trackId, string streamingId, string title, string artistName,
        string albumTitle, int rate, int depth, StreamingQuality quality, long durationMs)
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
            DurationMs = durationMs > 0 ? durationMs : 180000,
            Source = provider,
            StreamingId = streamingId,
            StreamingQuality = quality
        }, album, artist);
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
            StreamingProvider.Tidal => ("tr-tidal-time-out", "ar-brubeck", "Dave Brubeck", "al-time-out", "Time Out", "Take Five", 96000, 24, StreamingQuality.Max, "take-five-demo"),
            StreamingProvider.Local => throw new InvalidOperationException("local is not a streaming seed"),
            _ => throw Unexpected(provider)
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

    private void Load()
    {
        if (string.IsNullOrWhiteSpace(_storePath) || !File.Exists(_storePath)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_storePath));
            if (!doc.RootElement.TryGetProperty("accounts", out var accounts)) return;
            foreach (var el in accounts.EnumerateArray())
            {
                if (!el.TryGetProperty("provider", out var pEl)) continue;
                if (!Enum.TryParse<StreamingProvider>(pEl.GetString(), out var provider) || provider == StreamingProvider.Local)
                    continue;
                var token = el.TryGetProperty("token", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(token)) continue;
                var name = el.TryGetProperty("displayName", out var n) ? n.GetString() : provider.ToString();
                var refresh = el.TryGetProperty("refreshToken", out var r) ? r.GetString() : null;
                DateTimeOffset? exp = null;
                if (el.TryGetProperty("expiresAt", out var e) && DateTimeOffset.TryParse(e.GetString(), out var parsed))
                    exp = parsed;
                if (el.TryGetProperty("country", out var c) && provider == StreamingProvider.Tidal)
                    _tidalCountry = c.GetString() ?? _tidalCountry;
                Link(provider, token, name, refresh, exp, profile: null);
            }
        }
        catch { /* corrupt store */ }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_storePath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            object payload;
            lock (_gate)
            {
                payload = new
                {
                    country = _tidalCountry,
                    accounts = _accounts.Values.Select(a => new
                    {
                        provider = a.Provider.ToString(),
                        token = a.Token,
                        refreshToken = a.RefreshToken,
                        expiresAt = a.ExpiresAt,
                        displayName = a.DisplayName,
                        country = a.Provider == StreamingProvider.Tidal ? _tidalCountry : null
                    }).ToList()
                };
            }
            File.WriteAllText(_storePath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch { /* disk full / locked */ }
    }

    private static string Sanitize(string s)
        => new(s.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());

    private static string? Env(string key)
    {
        var v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private static ArgumentOutOfRangeException Unexpected(StreamingProvider provider)
        => new(nameof(provider), provider, null);

    private static bool InTestHost()
    {
        var entry = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "";
        var domain = AppDomain.CurrentDomain.FriendlyName;
        return entry.Contains("testhost", StringComparison.OrdinalIgnoreCase)
               || domain.Contains("testhost", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mono/0.1 (+https://github.com/GabrielJung0727/music-program)");
        return c;
    }

    private sealed record PendingAuth(string State, string Verifier);
}
