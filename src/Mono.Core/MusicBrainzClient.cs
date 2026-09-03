using System.Net.Http.Headers;
using System.Text.Json;

namespace Mono.Core;

/// <summary>
/// MusicBrainz artist alias 수집. User-Agent 필수, 초당 1요청 권장.
/// </summary>
public static class MusicBrainzClient
{
    private static readonly HttpClient Http = CreateHttp();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _lastCall = DateTimeOffset.MinValue;

    public static async Task<IReadOnlyList<string>> FetchAliasesAsync(string? mbid, string? name, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = TimeSpan.FromSeconds(1.1) - (DateTimeOffset.UtcNow - _lastCall);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct).ConfigureAwait(false);
            _lastCall = DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(mbid))
                return await FetchByMbidAsync(mbid!, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(name))
                return await SearchByNameAsync(name!, ct).ConfigureAwait(false);
            return [];
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<IReadOnlyList<string>> FetchByMbidAsync(string mbid, CancellationToken ct)
    {
        var url = $"https://musicbrainz.org/ws/2/artist/{Uri.EscapeDataString(mbid)}?inc=aliases&fmt=json";
        using var res = await Http.GetAsync(url, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return [];
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return ParseAliases(doc.RootElement);
    }

    private static async Task<IReadOnlyList<string>> SearchByNameAsync(string name, CancellationToken ct)
    {
        var url = $"https://musicbrainz.org/ws/2/artist?query=artist:{Uri.EscapeDataString(name)}&fmt=json&limit=1";
        using var res = await Http.GetAsync(url, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return [];
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("artists", out var artists) || artists.GetArrayLength() == 0)
            return [];
        var first = artists[0];
        if (first.TryGetProperty("id", out var id))
            return await FetchByMbidAsync(id.GetString()!, ct).ConfigureAwait(false);
        return ParseAliases(first);
    }

    private static List<string> ParseAliases(JsonElement artist)
    {
        var list = new List<string>();
        if (artist.TryGetProperty("name", out var name))
        {
            var n = name.GetString();
            if (!string.IsNullOrWhiteSpace(n)) list.Add(n!);
        }
        if (artist.TryGetProperty("sort-name", out var sort))
        {
            var s = sort.GetString();
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
        }
        if (artist.TryGetProperty("aliases", out var aliases))
        {
            foreach (var a in aliases.EnumerateArray())
            {
                if (a.TryGetProperty("name", out var an))
                {
                    var v = an.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v!);
                }
            }
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mono/0.1 (https://github.com/GabrielJung0727/music-program; contact=dev@localhost)");
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return c;
    }
}
