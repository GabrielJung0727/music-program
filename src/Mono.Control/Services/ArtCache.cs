using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Media.Imaging;

namespace Mono.Control.Services;

/// <summary>Core HTTP /api/art 비트맵 캐시. 그리드용 디코드 폭 제한 + LRU + 로드 계측.</summary>
public static class ArtCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LruGate = new();
    private static readonly LinkedList<string> Lru = new();
    private const int MaxEntries = 256;
    private const int GridDecodeWidth = 160;

    private static long _loads;
    private static long _hits;
    private static long _totalMs;

    public static long Loads => Interlocked.Read(ref _loads);
    public static long Hits => Interlocked.Read(ref _hits);
    public static double AverageLoadMs => Loads == 0 ? 0 : (double)Interlocked.Read(ref _totalMs) / Loads;

    public static string StatsText()
        => $"art cache hits={Hits} loads={Loads} avg={AverageLoadMs:F1}ms entries={Cache.Count}";

    public static Bitmap? TryGet(string? absoluteUrl)
    {
        if (string.IsNullOrWhiteSpace(absoluteUrl)) return null;
        if (!Cache.TryGetValue(absoluteUrl, out var e)) return null;
        Touch(absoluteUrl);
        Interlocked.Increment(ref _hits);
        return e.Bitmap;
    }

    public static async Task<Bitmap?> GetAsync(string? absoluteUrl, CancellationToken ct = default, int? decodeWidth = null)
    {
        if (string.IsNullOrWhiteSpace(absoluteUrl)) return null;
        if (Cache.TryGetValue(absoluteUrl, out var hit))
        {
            Touch(absoluteUrl);
            Interlocked.Increment(ref _hits);
            return hit.Bitmap;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var url = absoluteUrl;
            var w = decodeWidth ?? GridDecodeWidth;
            if (w > 0 && !url.Contains('?', StringComparison.Ordinal))
                url += $"?w={w}";

            await using var stream = await Http.GetStreamAsync(url, ct);
            await using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            ms.Position = 0;
            var bmp = Decode(ms, w);
            Put(absoluteUrl, bmp);
            return bmp;
        }
        catch
        {
            Put(absoluteUrl, null);
            return null;
        }
        finally
        {
            sw.Stop();
            Interlocked.Increment(ref _loads);
            Interlocked.Add(ref _totalMs, sw.ElapsedMilliseconds);
        }
    }

    private static Bitmap Decode(Stream ms, int decodeWidth)
    {
        if (decodeWidth > 0)
            return Bitmap.DecodeToWidth(ms, decodeWidth);
        return new Bitmap(ms);
    }

    private static void Put(string key, Bitmap? bmp)
    {
        Cache[key] = new CacheEntry(bmp);
        Touch(key);
        EvictIfNeeded();
    }

    private static void Touch(string key)
    {
        lock (LruGate)
        {
            Lru.Remove(key);
            Lru.AddFirst(key);
        }
    }

    private static void EvictIfNeeded()
    {
        lock (LruGate)
        {
            while (Lru.Count > MaxEntries)
            {
                var last = Lru.Last!.Value;
                Lru.RemoveLast();
                if (Cache.TryRemove(last, out var e))
                    e.Bitmap?.Dispose();
            }
        }
    }

    private sealed record CacheEntry(Bitmap? Bitmap);
}
