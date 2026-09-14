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

    public TidalProfile? FetchProfile(string accessToken)
    {
        using var doc = GetJson("https://openapi.tidal.com/v2/users/me", accessToken)
                        ?? GetJson("https://api.tidal.com/v1/users/me", accessToken);
        if (doc is null) return null;
        var root = doc.RootElement;
        var data = root.TryGetProperty("data", out var d) ? d : root;
        var attrs = data.TryGetProperty("attributes", out var a) ? a : data;
        var id = ReadId(data) ?? ReadId(root);
        var name = Str(attrs, "username") ?? Str(attrs, "displayName") ?? Str(root, "username") ?? "TIDAL";
        var country = Str(attrs, "country") ?? Str(root, "countryCode") ?? Str(root, "country") ?? "US";
        return new TidalProfile(id ?? "me", name, country);
    }

    public IReadOnlyList<TidalTrackHit> FetchFavorites(string accessToken, string country, int limit)
    {
        var hits = ParseTrackList(GetJson(
            $"https://api.tidal.com/v1/users/me/favorites/tracks?limit={limit}&countryCode={Uri.EscapeDataString(country)}",
            accessToken));
        if (hits.Count > 0) return hits;
        return ParseIncludedTracks(GetJson(
            $"https://openapi.tidal.com/v2/userCollections/me?countryCode={Uri.EscapeDataString(country)}&include=tracks",
            accessToken));
    }

    public IReadOnlyList<TidalTrackHit> Search(string accessToken, string query, string country, int limit)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];

        var v2 = GetJson(
            $"https://openapi.tidal.com/v2/searchResults/{Uri.EscapeDataString(q)}?countryCode={Uri.EscapeDataString(country)}&include=tracks,artists,albums",
            accessToken);
        var hits = ParseIncludedTracks(v2);
        if (hits.Count > 0) return hits.Take(limit).ToList();

        var v1 = GetJson(
            $"https://api.tidal.com/v1/search/tracks?query={Uri.EscapeDataString(q)}&limit={limit}&offset=0&countryCode={Uri.EscapeDataString(country)}",
            accessToken);
        return ParseTrackList(v1).Take(limit).ToList();
    }

    public string? FetchPlaybackUrl(string accessToken, string trackId, string country)
    {
        foreach (var quality in new[] { "HI_RES_LOSSLESS", "LOSSLESS", "HI_RES", "HIGH" })
        {
            var v2 = GetJson(
                $"https://openapi.tidal.com/v2/trackManifests/{Uri.EscapeDataString(trackId)}?countryCode={Uri.EscapeDataString(country)}&audioQuality={quality}",
                accessToken);
            var url = ExtractUrl(v2);
            if (url is not null) return url;

            var v1 = GetJson(
                $"https://api.tidal.com/v1/tracks/{Uri.EscapeDataString(trackId)}/playbackinfopostpaywall?playbackmode=STREAM&assetpresentation=FULL&audioquality={quality}&countryCode={Uri.EscapeDataString(country)}",
                accessToken);
            url = ExtractUrl(v1);
            if (url is not null) return url;
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
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Accept.ParseAdd("application/vnd.api+json");
            req.Headers.Accept.ParseAdd("application/json");
            using var res = _http.Send(req);
            if (!res.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(res.Content.ReadAsStream());
        }
        catch
        {
            return null;
        }
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

        if (list.Count == 0 && doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in data.EnumerateArray())
            {
                var hit = FromJsonApiTrack(node, artists, albums);
                if (hit is not null) list.Add(hit);
            }
        }

        return list;
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
        if (el.TryGetProperty("urls", out var urls) && urls.ValueKind == JsonValueKind.Array && urls.GetArrayLength() > 0)
        {
            var u = urls[0].GetString();
            if (!string.IsNullOrWhiteSpace(u) && u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return u;
        }

        foreach (var name in new[] { "url", "uri", "manifestUrl" })
        {
            var u = Str(el, name);
            if (!string.IsNullOrWhiteSpace(u) && u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return u;
        }

        var raw = Str(el, "manifest");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return raw;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            using var decoded = JsonDocument.Parse(json);
            return UrlFromElement(decoded.RootElement);
        }
        catch
        {
            return null;
        }
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

    private static string? Env(string key)
    {
        var v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}

internal sealed record TidalTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
internal sealed record TidalProfile(string UserId, string DisplayName, string Country);
internal sealed record TidalTrackHit(string Id, string Title, string Artist, string Album, long DurationMs);
