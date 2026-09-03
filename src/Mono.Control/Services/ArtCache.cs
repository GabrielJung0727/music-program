using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace Mono.Control.Services;

/// <summary>Core HTTP /api/art 비트맵 캐시.</summary>
public static class ArtCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? TryGet(string? absoluteUrl)
    {
        if (string.IsNullOrWhiteSpace(absoluteUrl)) return null;
        return Cache.TryGetValue(absoluteUrl, out var bmp) ? bmp : null;
    }

    public static async Task<Bitmap?> GetAsync(string? absoluteUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(absoluteUrl)) return null;
        if (Cache.TryGetValue(absoluteUrl, out var hit)) return hit;
        try
        {
            await using var stream = await Http.GetStreamAsync(absoluteUrl, ct);
            await using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            ms.Position = 0;
            var bmp = new Bitmap(ms);
            Cache[absoluteUrl] = bmp;
            return bmp;
        }
        catch
        {
            Cache[absoluteUrl] = null;
            return null;
        }
    }
}
