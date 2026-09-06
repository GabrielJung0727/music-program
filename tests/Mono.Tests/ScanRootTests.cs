using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>스캔 루트 복수화. 폴더를 여러 개 걸어도 한 카탈로그로 모인다.</summary>
public class ScanRootTests
{
    private static LibraryScanner NewScanner(string dir, out CatalogStore catalog)
    {
        catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        return new LibraryScanner(catalog, new ArtworkService(Path.Combine(dir, "art")));
    }

    [Fact]
    public void ScanAllCoversEveryRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        var a = Path.Combine(dir, "a");
        var b = Path.Combine(dir, "b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        TestAudio.WriteSilentWav(Path.Combine(a, "one.wav"));
        TestAudio.WriteSilentWav(Path.Combine(b, "two.wav"));

        var scanner = NewScanner(dir, out var catalog);

        var scanned = scanner.ScanAll([a, b]);

        Assert.Equal(2, scanned);
        Assert.Contains(catalog.Tracks.Values, t => t.LocalPath == Path.Combine(a, "one.wav"));
        Assert.Contains(catalog.Tracks.Values, t => t.LocalPath == Path.Combine(b, "two.wav"));
    }

    [Fact]
    public void MissingRootIsSkippedNotFatal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        var real = Path.Combine(dir, "real");
        Directory.CreateDirectory(real);
        TestAudio.WriteSilentWav(Path.Combine(real, "one.wav"));

        var scanner = NewScanner(dir, out _);

        // 없는 폴더가 섞여 있어도 나머지는 스캔된다 — 외장 드라이브가 빠졌다고 멈출 이유가 없다.
        var scanned = scanner.ScanAll([Path.Combine(dir, "gone"), real]);

        Assert.Equal(1, scanned);
        Assert.False(Directory.Exists(Path.Combine(dir, "gone")));
    }
}
