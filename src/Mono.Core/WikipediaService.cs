using System.Collections.Concurrent;
using System.Text.Json;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 위키백과에서 아티스트 이력을 가져와 라이너 패널에 짧게 소개한다.
/// 한국어(ko.wikipedia)를 먼저 시도하고 없으면 영어(en.wikipedia)로 폴백한다.
/// 저작권을 지키기 위해 요약 발췌 + 출처 링크만 보관하며, 전문을 복제하지 않는다.
/// 결과는 메모리 + 파일 캐시에 30일 보관해 같은 아티스트를 반복 조회하지 않는다.
/// </summary>
public sealed class WikipediaService
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);
    private static readonly string[] LangPreference = ["ko", "en"];

    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, ArtistWikiSummary?> _memCache = new();

    public WikipediaService(HttpClient http, string cacheDir)
    {
        _http = http;
        _cacheDir = cacheDir;
        Directory.CreateDirectory(cacheDir);
    }

    public async Task<ArtistWikiSummary?> GetSummaryAsync(string artistId, string artistName, CancellationToken ct = default)
    {
        if (_memCache.TryGetValue(artistId, out var cached) && IsFresh(cached))
        {
            return cached;
        }

        var fromDisk = ReadCacheFile(artistId);
        if (IsFresh(fromDisk))
        {
            _memCache[artistId] = fromDisk;
            return fromDisk;
        }

        ArtistWikiSummary? summary = null;
        foreach (var lang in LangPreference)
        {
            summary = await FetchAsync(lang, artistName, artistId, ct);
            if (summary is not null)
            {
                break;
            }
        }

        _memCache[artistId] = summary;
        if (summary is not null)
        {
            WriteCacheFile(artistId, summary);
        }

        return summary;
    }

    private static bool IsFresh(ArtistWikiSummary? summary)
        => summary is not null && DateTimeOffset.UtcNow - summary.FetchedAt < Ttl;

    private async Task<ArtistWikiSummary?> FetchAsync(string lang, string artistName, string artistId, CancellationToken ct)
    {
        try
        {
            var url = $"https://{lang}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(artistName)}";
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "disambiguation")
            {
                return null;
            }

            var extract = root.TryGetProperty("extract", out var e) ? e.GetString() : null;
            if (string.IsNullOrWhiteSpace(extract))
            {
                return null;
            }

            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? artistName : artistName;
            var pageUrl = root.TryGetProperty("content_urls", out var cu)
                && cu.TryGetProperty("desktop", out var d)
                && d.TryGetProperty("page", out var p)
                ? p.GetString()
                : null;
            var thumb = root.TryGetProperty("thumbnail", out var th) && th.TryGetProperty("source", out var s) ? s.GetString() : null;

            return new ArtistWikiSummary(
                artistId,
                title,
                extract!,
                pageUrl ?? $"https://{lang}.wikipedia.org/wiki/{Uri.EscapeDataString(artistName)}",
                thumb,
                lang,
                DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            // 네트워크 오류·타임아웃은 조용히 폴백 — 위키 소개는 부가 기능이다.
            return null;
        }
    }

    private ArtistWikiSummary? ReadCacheFile(string artistId)
    {
        try
        {
            var path = Path.Combine(_cacheDir, artistId + ".json");
            return File.Exists(path) ? JsonSerializer.Deserialize<ArtistWikiSummary>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void WriteCacheFile(string artistId, ArtistWikiSummary summary)
    {
        try
        {
            File.WriteAllText(Path.Combine(_cacheDir, artistId + ".json"), JsonSerializer.Serialize(summary, Json));
        }
        catch (Exception)
        {
            // 캐시 저장 실패는 무시 — 다음 호출에서 다시 받아온다.
        }
    }
}
