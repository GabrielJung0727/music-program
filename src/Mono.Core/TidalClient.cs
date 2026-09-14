using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// TIDAL Developer Platform (OAuth 2.1 PKCE + catalog/playback).
/// Client ID는 공개 클라이언트 값. Secret은 env에 있을 때만 토큰 교환에 붙인다.
/// </summary>
internal sealed class TidalClient
{
    public const string EmbeddedClientId = "iGGv2a5inmlTZdbY";
    public const string DefaultRedirect = "http://127.0.0.1:7702/oauth/callback";
    public const string Scopes = "collection.read entitlements.read playback playlists.read user.read search.read";

    private readonly HttpClient _http;

    public TidalClient(HttpClient http) => _http = http;

    public static string ClientId => Env("MONO_TIDAL_CLIENT_ID") ?? EmbeddedClientId;
    public static string? ClientSecret => Env("MONO_TIDAL_CLIENT_SECRET");
    public static string Redirect => Env("MONO_OAUTH_REDIRECT") ?? DefaultRedirect;
    public static bool DemoForced => string.Equals(Env("MONO_TIDAL_DEMO"), "1", StringComparison.OrdinalIgnoreCase);

    public string BuildAuthorizeUrl(string state, string challenge)
    {
        var q = new StringBuilder("https://login.tidal.com/authorize?response_type=code");
        q.Append("&client_id=").Append(Uri.EscapeDataString(ClientId));
        q.Append("&redirect_uri=").Append(Uri.EscapeDataString(Redirect));
        q.Append("&scope=").Append(Uri.EscapeDataString(Scopes));
        q.Append("&code_challenge_method=S256");
        q.Append("&code_challenge=").Append(Uri.EscapeDataString(challenge));
        q.Append("&state=").Append(Uri.EscapeDataString(state));
        return q.ToString();
    }

    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public TidalTokens? ExchangeCode(string code, string verifier)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = Redirect,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier
        };
        return PostToken(form);
    }

    public TidalTokens? Refresh(string refreshToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId
        };
        return PostToken(form);
    }

    public string? LastError { get; private set; }

    /// <summary>
    /// 직전 호출이 Tidal 한도(429)에 걸렸는지. "곡이 없음"과 구분해야 한다 —
    /// 전자는 잠시 뒤 다시 하면 되고, 후자는 다시 해도 같다.
    /// </summary>
    public bool RateLimited { get; private set; }

    /// <summary>Retry-After 가 이보다 길면 기다리지 않는다. 사용자를 세워 둘 수는 없다.</summary>
    private static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(2);

    /// <summary>한도에 걸렸을 때 같은 URL 을 다시 시도하는 횟수.</summary>
    private const int RateLimitRetries = 1;

    public TidalProfile FetchProfile(string accessToken)
    {
        var jwt = ProfileFromJwt(accessToken);
        using var doc = GetJson("https://openapi.tidal.com/v2/users/me?include=entitlements", accessToken);
        if (doc is not null)
        {
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var d) ? d : root;
            var attrs = data.TryGetProperty("attributes", out var a) ? a : data;
            var id = ReadId(data) ?? jwt?.UserId ?? "me";
            var name = Str(attrs, "username") ?? Str(attrs, "displayName") ?? jwt?.DisplayName ?? "TIDAL";
            var country = Str(attrs, "country") ?? Str(attrs, "countryCode") ?? jwt?.Country ?? "US";
            return new TidalProfile(id, name, country);
        }

        return jwt ?? new TidalProfile("me", "TIDAL", "US");
    }

    public IReadOnlyList<TidalTrackHit> FetchFavorites(string accessToken, TidalProfile profile, int limit)
    {
        foreach (var cc in Countries(profile.Country))
        {
            foreach (var path in new[]
            {
                $"https://openapi.tidal.com/v2/userCollections/{Uri.EscapeDataString(profile.UserId)}/relationships/tracks?countryCode={cc}&include=tracks,artists,albums",
                $"https://openapi.tidal.com/v2/userCollections/{Uri.EscapeDataString(profile.UserId)}?countryCode={cc}&include=tracks,artists,albums"
            })
            {
                var hits = ParseIncludedTracks(GetJson(path, accessToken));
                if (hits.Count > 0) return hits.Take(limit).ToList();
                if (RateLimited) return [];
            }
        }

        return [];
    }

    public IReadOnlyList<TidalTrackHit> Search(string accessToken, string query, string country, int limit)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];
        var encoded = Uri.EscapeDataString(q);

        foreach (var cc in Countries(country))
        {
            foreach (var url in new[]
            {
                $"https://openapi.tidal.com/v2/searchResults/{encoded}/relationships/tracks?countryCode={cc}&include=tracks,artists,albums",
                $"https://openapi.tidal.com/v2/searchResults/{encoded}?countryCode={cc}&include=tracks,artists,albums,topHits",
                $"https://openapi.tidal.com/v2/searchResults?filter[query]={encoded}&countryCode={cc}&include=tracks,artists,albums"
            })
            {
                var hits = ParseIncludedTracks(GetJson(url, accessToken));
                if (hits.Count > 0) return hits.Take(limit).ToList();
                // 한도에 걸렸다면 남은 조합도 똑같이 거절당한다. 더 두드릴수록 한도만 깊어진다.
                if (RateLimited) return [];
            }
        }

        return [];
    }

    public string? FetchPlaybackUrl(string accessToken, string trackId, string country)
    {
        foreach (var cc in Countries(country))
        {
            foreach (var quality in new[] { "HI_RES_LOSSLESS", "LOSSLESS", "HI_RES", "HIGH" })
            {
                var v2 = GetJson(
                    $"https://openapi.tidal.com/v2/trackManifests/{Uri.EscapeDataString(trackId)}?countryCode={cc}&audioQuality={quality}",
                    accessToken);
                var url = ExtractUrl(v2);
                if (url is not null) return url;
            }

            foreach (var quality in new[] { "HI_RES", "LOSSLESS", "HIGH" })
            {
                var v1 = GetJson(
                    $"https://api.tidal.com/v1/tracks/{Uri.EscapeDataString(trackId)}/playbackinfopostpaywall?playbackmode=STREAM&assetpresentation=FULL&audioquality={quality}&countryCode={cc}",
                    accessToken);
                var url = ExtractUrl(v1);
                if (url is not null) return url;
            }
        }

        return null;
    }

    private TidalTokens? PostToken(Dictionary<string, string> form)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.tidal.com/v1/oauth2/token");
        var secret = ClientSecret;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{secret}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        req.Content = new FormUrlEncodedContent(form);
        using var res = _http.Send(req);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(res.Content.ReadAsStream());
        var root = doc.RootElement;
        var access = Str(root, "access_token");
        if (string.IsNullOrWhiteSpace(access)) return null;
        var refresh = Str(root, "refresh_token");
        var expires = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var sec)
            ? DateTimeOffset.UtcNow.AddSeconds(Math.Max(sec - 60, 30))
            : DateTimeOffset.UtcNow.AddHours(1);
        return new TidalTokens(access, refresh, expires);
    }

    private JsonDocument? GetJson(string url, string accessToken)
    {
        LastError = null;
        RateLimited = false;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                req.Headers.TryAddWithoutValidation("Accept", "application/vnd.api+json");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var res = _http.Send(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                using var reader = new StreamReader(res.Content.ReadAsStream());
                var body = reader.ReadToEnd();

                if (res.IsSuccessStatusCode)
                {
                    // 재시도가 통했으면 결국 막히지 않은 것이다. RateLimited 는
                    // "이번 호출이 한도 때문에 포기했다"는 뜻이어야 한다.
                    RateLimited = false;
                    LastError = null;
                    return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                }

                var snippet = body.Length <= 200 ? body : body[..200];
                LastError = $"{(int)res.StatusCode} {url} {snippet}";

                if (res.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    return null;
                }

                // 429. 서버가 곧 풀린다고 하면 한 번만 기다렸다 다시 해 본다.
                RateLimited = true;
                var wait = RetryAfter(res);
                if (attempt >= RateLimitRetries || wait is null || wait > MaxRetryWait)
                {
                    return null;
                }

                Thread.Sleep(wait.Value);
            }
            catch (Exception ex)
            {
                LastError = $"{url} {ex.GetType().Name}: {ex.Message}";
                return null;
            }
        }
    }

    /// <summary>Retry-After 를 초 또는 날짜 형식 모두에서 읽는다. 없으면 null.</summary>
    private static TimeSpan? RetryAfter(HttpResponseMessage res)
    {
        var header = res.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    private static List<TidalTrackHit> ParseTrackList(JsonDocument? doc)
    {
        var list = new List<TidalTrackHit>();
        if (doc is null) return list;
        var root = doc.RootElement;
        JsonElement items = default;
        if (root.TryGetProperty("items", out var arr)) items = arr;
        else if (root.TryGetProperty("tracks", out var tracks) && tracks.TryGetProperty("items", out var inner)) items = inner;
        if (items.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in items.EnumerateArray())
        {
            var track = item.TryGetProperty("item", out var nested) ? nested : item;
            var hit = FromTrackObject(track);
            if (hit is not null) list.Add(hit);
        }

        return list;
    }

    private static List<TidalTrackHit> ParseIncludedTracks(JsonDocument? doc)
    {
        var list = new List<TidalTrackHit>();
        if (doc is null) return list;
        var artists = new Dictionary<string, string>(StringComparer.Ordinal);
        var albums = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("included", out var included) && included.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in included.EnumerateArray())
            {
                var type = Str(node, "type") ?? "";
                var id = ReadId(node);
                if (id is null) continue;
                var attrs = node.TryGetProperty("attributes", out var a) ? a : node;
                var name = Str(attrs, "name") ?? Str(attrs, "title") ?? id;
                if (type.Contains("artist", StringComparison.OrdinalIgnoreCase))
                    artists[id] = name;
                else if (type.Contains("album", StringComparison.OrdinalIgnoreCase))
                    albums[id] = name;
            }

            foreach (var node in included.EnumerateArray())
            {
                var type = Str(node, "type") ?? "";
                if (!type.Contains("track", StringComparison.OrdinalIgnoreCase)) continue;
                var hit = FromJsonApiTrack(node, artists, albums);
                if (hit is not null) list.Add(hit);
            }
        }

        if (list.Count == 0 && doc.RootElement.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in data.EnumerateArray())
                    AddIfTrack(node);
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                AddIfTrack(data);
            }
        }

        return list;

        void AddIfTrack(JsonElement node)
        {
            var type = Str(node, "type") ?? "tracks";
            if (!type.Contains("track", StringComparison.OrdinalIgnoreCase)
                || type.Contains("manifest", StringComparison.OrdinalIgnoreCase))
                return;
            var hit = FromJsonApiTrack(node, artists, albums);
            if (hit is not null) list.Add(hit);
        }
    }

    private static TidalTrackHit? FromJsonApiTrack(JsonElement node, Dictionary<string, string> artists, Dictionary<string, string> albums)
    {
        var id = ReadId(node);
        if (id is null) return null;
        var attrs = node.TryGetProperty("attributes", out var a) ? a : node;
        var title = Str(attrs, "title") ?? Str(attrs, "name") ?? "Track";
        var duration = DurationMs(attrs);
        var artist = "TIDAL";
        var album = "TIDAL";
        if (node.TryGetProperty("relationships", out var rel))
        {
            artist = RelName(rel, "artists", artists) ?? artist;
            album = RelName(rel, "albums", albums) ?? album;
        }

        return new TidalTrackHit(id, title, artist, album, duration);
    }

    private static string? RelName(JsonElement rel, string key, Dictionary<string, string> names)
    {
        if (!rel.TryGetProperty(key, out var block)) return null;
        var data = block.TryGetProperty("data", out var d) ? d : block;
        if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var id = ReadId(data[0]);
            return id is not null && names.TryGetValue(id, out var n) ? n : null;
        }

        if (data.ValueKind == JsonValueKind.Object)
        {
            var id = ReadId(data);
            return id is not null && names.TryGetValue(id, out var n) ? n : null;
        }

        return null;
    }

    private static TidalTrackHit? FromTrackObject(JsonElement track)
    {
        var id = ReadId(track);
        if (id is null) return null;
        var title = Str(track, "title") ?? "Track";
        var artist = "TIDAL";
        if (track.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array && artists.GetArrayLength() > 0)
            artist = Str(artists[0], "name") ?? artist;
        else if (track.TryGetProperty("artist", out var one))
            artist = Str(one, "name") ?? artist;
        var album = "TIDAL";
        if (track.TryGetProperty("album", out var al))
            album = Str(al, "title") ?? album;
        return new TidalTrackHit(id, title, artist, album, DurationMs(track));
    }

    private static string? ExtractUrl(JsonDocument? doc)
    {
        if (doc is null) return null;
        var root = doc.RootElement;
        if (root.TryGetProperty("data", out var data))
        {
            var attrs = data.TryGetProperty("attributes", out var a) ? a : data;
            var fromAttrs = UrlFromElement(attrs);
            if (fromAttrs is not null) return fromAttrs;
        }

        return UrlFromElement(root);
    }

    private static string? UrlFromElement(JsonElement el)
    {
        var raw = Str(el, "manifest");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return ScoreStreamUrl(raw) > -500 ? raw : null;
            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
                using var decoded = JsonDocument.Parse(json);
                var fromManifest = PickBestHttpUrl(decoded.RootElement);
                if (fromManifest is not null) return fromManifest;
            }
            catch { /* fall through */ }
        }

        var fromTree = PickBestHttpUrl(el);
        return fromTree;
    }

    /// <summary>NAudio는 HLS(m3u8)보다 progressive FLAC/MP4 URL을 재생할 수 있다.</summary>
    internal static string? PickBestHttpUrl(JsonElement root)
    {
        var candidates = new List<string>();
        CollectHttpUrls(root, candidates);
        if (candidates.Count == 0) return null;

        string? best = null;
        var bestScore = int.MinValue;
        foreach (var url in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var score = ScoreStreamUrl(url);
            if (score > bestScore)
            {
                bestScore = score;
                best = url;
            }
        }

        return bestScore > -500 ? best : null;
    }

    private static void CollectHttpUrls(JsonElement el, List<string> sink)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s) && s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    sink.Add(s);
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    CollectHttpUrls(item, sink);
                break;
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                    CollectHttpUrls(prop.Value, sink);
                break;
        }
    }

    internal static int ScoreStreamUrl(string url)
    {
        var u = url.ToLowerInvariant();
        var score = 0;
        if (u.Contains(".flac", StringComparison.Ordinal)) score += 120;
        if (u.Contains(".mp4", StringComparison.Ordinal) && !u.Contains(".m3u8", StringComparison.Ordinal)) score += 80;
        if (u.Contains("audio", StringComparison.Ordinal)) score += 20;
        if (u.Contains(".m3u8", StringComparison.Ordinal) || u.Contains("mpegurl", StringComparison.Ordinal)) score -= 200;
        if (u.Contains("/master.", StringComparison.Ordinal) || u.Contains("/playlist.", StringComparison.Ordinal)) score -= 150;
        return score;
    }

    private static long DurationMs(JsonElement el)
    {
        if (el.TryGetProperty("duration", out var d))
        {
            if (d.ValueKind == JsonValueKind.Number && d.TryGetInt64(out var n))
                return n > 1000 ? n : n * 1000;
            if (d.ValueKind == JsonValueKind.String && TimeSpan.TryParse(d.GetString(), out var ts))
                return (long)ts.TotalMilliseconds;
        }

        return 180_000;
    }

    private static IEnumerable<string> Countries(string? preferred)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cc in new[] { preferred, "WW", "US" })
        {
            if (string.IsNullOrWhiteSpace(cc) || !seen.Add(cc)) continue;
            yield return cc;
        }
    }

    private static TidalProfile? ProfileFromJwt(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var id = root.TryGetProperty("userId", out var uid)
                ? uid.ToString()
                : Str(root, "uid") ?? Str(root, "sub") ?? "me";
            var country = Str(root, "countryCode") ?? Str(root, "country") ?? "US";
            var name = Str(root, "username") ?? Str(root, "name") ?? "TIDAL";
            return new TidalProfile(id, name, country);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private static string? ReadId(JsonElement el)
    {
        if (!el.TryGetProperty("id", out var id)) return null;
        return id.ValueKind switch
        {
            JsonValueKind.String => id.GetString(),
            JsonValueKind.Number => id.ToString(),
            _ => null
        };
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Env(string key) => CredentialStore.Get(key);
}

internal sealed record TidalTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
internal sealed record TidalProfile(string UserId, string DisplayName, string Country);
internal sealed record TidalTrackHit(string Id, string Title, string Artist, string Album, long DurationMs);
