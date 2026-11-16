using Mono.Core;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 곡 id 는 재생 기록 · 플레이리스트 · 커버 캐시가 곡을 가리키는 열쇠다.
///
/// 예전에는 스캔할 때마다 파일 이름에서 id 를 계산했다. 앨범마다 "01 Intro.flac" 이 있으면
/// 모두 같은 id 가 되어 마지막에 스캔된 곡만 남았다. 이제 id 는 한 번 발급하면 그 파일을 따라간다.
/// 파일이 옮겨졌으면 태그가 분명히 맞을 때만 옛 id 를 이어 준다. 틀린 곡에 붙으면 되돌릴 수 없다.
/// </summary>
public class TrackIdentityTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Tagged(string path, string? title, string? album, uint track)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TestAudio.WriteSilentWav(path);
        using var tf = TagLib.File.Create(path);
        tf.Tag.Title = title;
        tf.Tag.Album = album;
        tf.Tag.Performers = ["Test Artist"];
        tf.Tag.Track = track;
        tf.Save();
        return path;
    }

    /// <summary>Core 를 새로 띄운 것과 같다. 같은 DB 를 새 인스턴스로 연다.</summary>
    private static (CatalogStore Catalog, LibraryScanner Scanner) Start(string dir)
    {
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        return (catalog, new LibraryScanner(catalog, new ArtworkService(Path.Combine(dir, "art"))));
    }

    private static string IdAt(CatalogStore catalog, string path)
        => catalog.Tracks.Values.Single(t => string.Equals(t.LocalPath, path, StringComparison.OrdinalIgnoreCase)).Id;

    [Fact]
    public void TheSameFileNameInTwoAlbumsGivesTwoTracks()
    {
        var dir = NewDir();
        var lib = Path.Combine(dir, "lib");
        var a = Tagged(Path.Combine(lib, "A", "01 Intro.wav"), "Intro", "Album A", 1);
        var b = Tagged(Path.Combine(lib, "B", "01 Intro.wav"), "Intro", "Album B", 1);
        var (catalog, scanner) = Start(dir);

        scanner.Scan(lib);

        Assert.NotEqual(IdAt(catalog, a), IdAt(catalog, b));
        Assert.Equal(2, catalog.Tracks.Values.Count(t => t.Title == "Intro"));
    }

    [Fact]
    public void ARescanAfterARestartKeepsEveryId()
    {
        var dir = NewDir();
        var lib = Path.Combine(dir, "lib");
        var a = Tagged(Path.Combine(lib, "A", "01 Intro.wav"), "Intro", "Album A", 1);
        var b = Tagged(Path.Combine(lib, "B", "01 Intro.wav"), "Intro", "Album B", 1);
        var (first, firstScanner) = Start(dir);
        firstScanner.Scan(lib);
        var before = (IdAt(first, a), IdAt(first, b));

        var (second, secondScanner) = Start(dir);
        secondScanner.Scan(lib);

        Assert.Equal(before, (IdAt(second, a), IdAt(second, b)));
    }

    [Fact]
    public void AFileWhoseNameIsUniqueKeepsTheIdOlderVersionsGaveIt()
    {
        // 이미 쌓인 재생 기록과 플레이리스트가 그대로 맞으려면, 겹치지 않는 곡의 id 는 바뀌면 안 된다.
        var dir = NewDir();
        var lib = Path.Combine(dir, "lib");
        var song = Tagged(Path.Combine(lib, "A", "Blue in Green.wav"), "Blue in Green", "Album A", 3);
        var (catalog, scanner) = Start(dir);

        scanner.Scan(lib);

        Assert.Equal("tr-blue-in-green", IdAt(catalog, song));
    }

    [Fact]
    public void AMovedFileKeepsItsIdWhenItsTagsMatch()
    {
        var dir = NewDir();
        var lib = Path.Combine(dir, "lib");
        var old = Tagged(Path.Combine(lib, "A", "01 Intro.wav"), "Intro", "Album A", 1);
        var (first, firstScanner) = Start(dir);
        firstScanner.Scan(lib);
        var id = IdAt(first, old);

        var moved = Path.Combine(lib, "A", "01 - Intro.wav");
        File.Move(old, moved);
        var (second, secondScanner) = Start(dir);
        secondScanner.Scan(lib);

        Assert.Equal(id, IdAt(second, moved));
        Assert.DoesNotContain(second.Tracks.Values, t => string.Equals(t.LocalPath, old, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AMovedFileWithoutATrackNumberGetsANewId()
    {
        // 태그가 하나라도 비어 있으면 태그로는 무엇과 같은 곡인지 말할 근거가 모자란다.
        // 파일 이름까지 바뀌었으면 붙이지 않는다(이름이 그대로인 경우는 아래 FolderRename 테스트).
        var dir = NewDir();
        var lib = Path.Combine(dir, "lib");
        var old = Tagged(Path.Combine(lib, "A", "take.wav"), "Take", "Album A", 0);
        var (first, firstScanner) = Start(dir);
        firstScanner.Scan(lib);
        var id = IdAt(first, old);

        var moved = Path.Combine(lib, "A", "take-final.wav");
        File.Move(old, moved);
        var (second, secondScanner) = Start(dir);
        secondScanner.Scan(lib);

        Assert.NotEqual(id, IdAt(second, moved));
    }

    [Fact]
    public void WhenTwoMissingTracksLookTheSameTheMovedFileGetsANewId()
    {
        // 후보가 둘 이상이면 어느 쪽인지 고를 수 없다. 틀린 곡에 붙느니 새 곡으로 둔다.
        var dir = NewDir();
        var lib = Path.Combine(dir, "lib");
        var x = Tagged(Path.Combine(lib, "X", "a.wav"), "Same", "Album X", 1);
        var y = Tagged(Path.Combine(lib, "X", "b.wav"), "Same", "Album X", 1);
        var (first, firstScanner) = Start(dir);
        firstScanner.Scan(lib);
        var ids = new[] { IdAt(first, x), IdAt(first, y) };

        File.Delete(x);
        File.Delete(y);
        var moved = Tagged(Path.Combine(lib, "X", "c.wav"), "Same", "Album X", 1);
        var (second, secondScanner) = Start(dir);
        secondScanner.Scan(lib);

        Assert.DoesNotContain(IdAt(second, moved), ids);
    }

    [Fact]
    public void AFileThatStillExistsIsNeverTakenOverByAnother()
    {
        // 옛 id 를 이어 받는 후보는 파일이 사라진 곡뿐이다. 멀쩡히 있는 곡의 자리를 뺏지 않는다.
        var dir = NewDir();
        var lib = Path.Combine(dir, "lib");
        var original = Tagged(Path.Combine(lib, "A", "01 Intro.wav"), "Intro", "Album A", 1);
        var (first, firstScanner) = Start(dir);
        firstScanner.Scan(lib);
        var id = IdAt(first, original);

        var copy = Path.Combine(lib, "A", "copy", "01 Intro (copy).wav");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(original, copy);
        var (second, secondScanner) = Start(dir);
        secondScanner.Scan(lib);

        Assert.Equal(id, IdAt(second, original));
        Assert.NotEqual(id, IdAt(second, copy));
    }

    [Fact]
    public void AFolderRenameKeepsTheIdsOfFilesWithoutFullTags()
    {
        // 싱글이나 직접 뜬 파일은 트랙 번호가 없는 일이 흔하다. 폴더 이름만 바뀌었는데 새 곡이 되면
        // 기록과 플레이리스트가 재생할 수 없는 옛 줄에 남는다. 파일 이름이 같고 옛 파일이 사라졌으면 잇는다 — 예전 동작이다.
        var dir = NewDir();
        var lib = Path.Combine(dir, "lib");
        var old = Tagged(Path.Combine(lib, "Singles", "Some Song.wav"), null, null, 0);
        var (first, firstScanner) = Start(dir);
        firstScanner.Scan(lib);
        var id = IdAt(first, old);

        Directory.Move(Path.Combine(lib, "Singles"), Path.Combine(lib, "Singles 2024"));
        var renamed = Path.Combine(lib, "Singles 2024", "Some Song.wav");
        var (second, secondScanner) = Start(dir);
        secondScanner.Scan(lib);

        Assert.Equal(id, IdAt(second, renamed));
        Assert.Single(second.Tracks.Values, t => t.Title == "Some Song");
    }

    [Fact]
    public void UntaggedTwinsThatBothExistStayTwoTracks()
    {
        // 이름으로 잇는 것은 옛 파일이 사라졌을 때뿐이다. 둘 다 살아 있으면 각자 곡이다.
        var dir = NewDir();
        var lib = Path.Combine(dir, "lib");
        var a = Tagged(Path.Combine(lib, "Rips A", "Track 01.wav"), null, null, 0);
        var (first, firstScanner) = Start(dir);
        firstScanner.Scan(lib);
        var b = Tagged(Path.Combine(lib, "Rips B", "Track 01.wav"), null, null, 0);
        var (second, secondScanner) = Start(dir);
        secondScanner.Scan(lib);

        Assert.NotEqual(IdAt(second, a), IdAt(second, b));
    }

    [Fact]
    public void TwoScansAtOnceGiveEachFileOneTrack()
    {
        // 시작할 때 · 30분마다 · 설정의 "다시 스캔" · 폴더 추가가 겹칠 수 있다.
        var dir = NewDir();
        var lib = Path.Combine(dir, "lib");
        Directory.CreateDirectory(lib);
        for (var i = 0; i < 200; i++) TinyWav(Path.Combine(lib, $"song-{i:000}.wav"));
        var (catalog, scanner) = Start(dir);

        var a = Task.Run(() => scanner.Scan(lib));
        var b = Task.Run(() => scanner.Scan(lib));
        Task.WaitAll(a, b);

        // 새 카탈로그에는 경로 없는 기본 곡이 들어 있다. 이 라이브러리의 파일만 센다.
        var perPath = catalog.Tracks.Values
            .Where(t => t.LocalPath is not null && t.LocalPath.StartsWith(lib, StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.LocalPath, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(200, perPath.Count);
        Assert.All(perPath, g => Assert.Single(g));
    }

    /// <summary>스캔 동시성 시험용 아주 짧은 무음 WAV. 200개를 만들어도 몇 MB 를 넘지 않는다.</summary>
    private static void TinyWav(string path)
    {
        const int rate = 8000, channels = 1, bits = 16, frames = 80;
        var dataLen = frames * channels * (bits / 8);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(rate); w.Write(rate * channels * (bits / 8)); w.Write((short)(channels * (bits / 8))); w.Write((short)bits);
        w.Write("data"u8.ToArray()); w.Write(dataLen); w.Write(new byte[dataLen]);
    }
}
