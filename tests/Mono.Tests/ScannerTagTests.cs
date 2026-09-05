using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>스캐너가 장르·작곡가 태그를 카탈로그로 옮기는지 확인한다.</summary>
public class ScannerTagTests
{
    [Fact]
    public void ScanReadsGenreAndComposerTags()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var lib = Path.Combine(dir, "library");
        Directory.CreateDirectory(lib);

        var wav = Path.Combine(lib, "tagged.wav");
        TestAudio.WriteSilentWav(wav);
        using (var tf = TagLib.File.Create(wav))
        {
            tf.Tag.Title = "Symphony No. 5 in C minor, Op. 67: I. Allegro con brio";
            tf.Tag.Genres = ["Classical", "Symphony"];
            tf.Tag.Composers = ["Ludwig van Beethoven"];
            tf.Tag.Album = "Beethoven: Symphonies";
            tf.Save();
        }

        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var scanner = new LibraryScanner(catalog, new ArtworkService(Path.Combine(dir, "art")));
        scanner.Scan(lib);

        var track = catalog.Tracks.Values.Single(t => t.LocalPath == wav);
        Assert.Equal(["Classical", "Symphony"], track.Genres);
        Assert.Equal(["Ludwig van Beethoven"], track.Composers);
    }
}

/// <summary>테스트용 무음 WAV 생성. 여러 테스트가 공유한다.</summary>
internal static class TestAudio
{
    public static void WriteSilentWav(string path)
    {
        const int rate = 44100, channels = 2, bits = 16, seconds = 1;
        var dataLen = rate * channels * (bits / 8) * seconds;
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataLen);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * (bits / 8));
        w.Write((short)(channels * (bits / 8)));
        w.Write((short)bits);
        w.Write("data"u8.ToArray());
        w.Write(dataLen);
        w.Write(new byte[dataLen]);
    }
}
