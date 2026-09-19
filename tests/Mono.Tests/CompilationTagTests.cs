using Mono.Shared;
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 컴필레이션 앨범의 아티스트.
///
/// 한 장에 여러 연주자가 실린 음반은 앨범 아티스트가 "Various Artists" 같은 묶음 이름이다.
/// 그걸 트랙의 아티스트로 올리면 어느 곡을 틀어도 누가 연주했는지 알 수 없다.
/// 반대로 참여 음악가로 앨범을 묶으면 한 장이 참여자 수만큼 쪼개진다. 둘을 나눠 쓴다.
/// </summary>
public class CompilationTagTests
{
    private static (CatalogStore Catalog, string Lib) NewLibrary()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        var lib = Path.Combine(dir, "library");
        Directory.CreateDirectory(lib);
        return (new CatalogStore(Path.Combine(dir, "c.db")), lib);
    }

    private static void WriteTagged(string path, string title, string? albumArtist, string? performer, string album)
    {
        TestAudio.WriteSilentWav(path);
        using var tf = TagLib.File.Create(path);
        tf.Tag.Title = title;
        tf.Tag.Album = album;
        if (albumArtist is not null) tf.Tag.AlbumArtists = [albumArtist];
        if (performer is not null) tf.Tag.Performers = [performer];
        tf.Save();
    }

    private static LibraryScanner ScannerFor(CatalogStore catalog, string lib)
        => new(catalog, new ArtworkService(Path.Combine(Path.GetDirectoryName(lib)!, "art")));

    /// <summary>트랙의 아티스트는 참여 음악가다.</summary>
    [Fact]
    public void TheTrackArtistIsThePerformer()
    {
        var (catalog, lib) = NewLibrary();
        WriteTagged(Path.Combine(lib, "one.wav"), "Track One", "Various Artists", "Bill Evans", "Jazz Sampler");
        ScannerFor(catalog, lib).Scan(lib);

        var track = catalog.Tracks.Values.Single(t => t.Title == "Track One");
        Assert.Equal("Bill Evans", catalog.Artists[track.ArtistId].Name);
    }

    /// <summary>앨범은 여전히 앨범 아티스트로 묶인다 — 컴필레이션 한 장이 쪼개지면 안 된다.</summary>
    [Fact]
    public void TheAlbumStaysOneAlbumUnderTheAlbumArtist()
    {
        var (catalog, lib) = NewLibrary();
        WriteTagged(Path.Combine(lib, "one.wav"), "Track One", "Various Artists", "Bill Evans", "Jazz Sampler");
        WriteTagged(Path.Combine(lib, "two.wav"), "Track Two", "Various Artists", "Chet Baker", "Jazz Sampler");
        ScannerFor(catalog, lib).Scan(lib);

        var albums = catalog.Albums.Values.Where(a => a.Title == "Jazz Sampler").ToList();
        var album = Assert.Single(albums);
        Assert.Equal("Various Artists", catalog.Artists[album.ArtistId].Name);

        var tracks = catalog.Tracks.Values.Where(t => t.AlbumId == album.Id).ToList();
        Assert.Equal(2, tracks.Count);
        Assert.Equal(
            ["Bill Evans", "Chet Baker"],
            tracks.Select(t => catalog.Artists[t.ArtistId].Name).OrderBy(n => n).ToArray());
    }

    /// <summary>참여 음악가가 없으면 앨범 아티스트를 쓴다.</summary>
    [Fact]
    public void TheAlbumArtistIsUsedWhenNoPerformerIsTagged()
    {
        var (catalog, lib) = NewLibrary();
        WriteTagged(Path.Combine(lib, "solo.wav"), "Solo", "Keith Jarrett", null, "The Köln Concert");
        ScannerFor(catalog, lib).Scan(lib);

        var track = catalog.Tracks.Values.Single(t => t.Title == "Solo");
        Assert.Equal("Keith Jarrett", catalog.Artists[track.ArtistId].Name);
    }

    /// <summary>
    /// 이미 옛 규칙으로 들어와 있던 곡도 다시 훑으면 고쳐진다 — 스캔은 30분마다,
    /// 기동 직후에도 한 번 돈다. 업데이트한 사람이 아무것도 하지 않아도 제자리를 찾는다.
    /// </summary>
    [Fact]
    public void ARescanRepointsTracksToThePerformer()
    {
        var (catalog, lib) = NewLibrary();
        var path = Path.Combine(lib, "one.wav");
        WriteTagged(path, "Track One", "Various Artists", "Bill Evans", "Jazz Sampler");
        var scanner = ScannerFor(catalog, lib);
        scanner.Scan(lib);

        // 옛 규칙(앨범 아티스트가 트랙 아티스트)으로 되돌려 둔다.
        var track = catalog.Tracks.Values.Single(t => t.Title == "Track One");
        var album = catalog.Albums[track.AlbumId];
        catalog.UpsertTrack(
            new Track
            {
                Id = track.Id,
                Title = track.Title,
                AlbumId = track.AlbumId,
                ArtistId = album.ArtistId,
                LocalPath = track.LocalPath
            },
            album,
            catalog.Artists[album.ArtistId]);
        Assert.Equal("Various Artists", catalog.Artists[catalog.Tracks[track.Id].ArtistId].Name);

        scanner.Scan(lib);

        Assert.Equal("Bill Evans", catalog.Artists[catalog.Tracks[track.Id].ArtistId].Name);
    }
}
