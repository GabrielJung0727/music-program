using Microsoft.Data.Sqlite;
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 스키마가 늘어도 기존 사용자가 재스캔을 강요당하면 안 된다.
/// 구 스키마 DB를 열어 새 컬럼이 빈 값으로 시작하는지 확인한다.
/// </summary>
public class CatalogMigrationTests
{
    private static string TempDb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "c.db");
    }

    [Fact]
    public void OldSchemaOpensWithoutRescan()
    {
        var path = TempDb();

        // genres/composers 가 없던 시절의 스키마로 DB를 만들고 트랙 한 줄을 넣는다.
        using (var con = new SqliteConnection($"Data Source={path}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE artists (id TEXT PRIMARY KEY, name TEXT, related TEXT, bio TEXT, aliases TEXT);
                CREATE TABLE albums (id TEXT PRIMARY KEY, title TEXT, artist_id TEXT, liner TEXT, label TEXT, year INT, credits TEXT, art TEXT);
                CREATE TABLE tracks (
                  id TEXT PRIMARY KEY, title TEXT, album_id TEXT, artist_id TEXT,
                  local_path TEXT, streaming_id TEXT, source INT, quality INT,
                  sample_rate INT, bit_depth INT, channels INT, is_dsd INT, dsd_rate INT,
                  duration_ms INT, lyrics TEXT, art TEXT, merged INT, track_no INT DEFAULT 0);
                INSERT INTO artists (id, name) VALUES ('ar-1', 'Old Artist');
                INSERT INTO albums (id, title, artist_id) VALUES ('al-1', 'Old Album', 'ar-1');
                -- 구 버전 앱이 실제로 쓰던 대로 채운다. Load 는 이 컬럼들이 NULL 이 아니라고 가정한다.
                INSERT INTO tracks
                  (id, title, album_id, artist_id, source, quality, sample_rate, bit_depth,
                   channels, is_dsd, duration_ms, merged, track_no)
                  VALUES ('tr-old', 'Old Track', 'al-1', 'ar-1', 0, 0, 44100, 16, 2, 0, 1000, 0, 1);
                """;
            cmd.ExecuteNonQuery();
        }

        var store = new CatalogStore(path);

        var track = store.Tracks["tr-old"];
        Assert.Equal("Old Track", track.Title);
        Assert.Empty(track.Genres);
        Assert.Empty(track.Composers);
    }

    [Fact]
    public void GenresAndComposersRoundTripThroughTheDatabase()
    {
        var path = TempDb();
        var store = new CatalogStore(path);
        var seeded = store.Tracks.Values.First();
        seeded.Genres.Clear();
        seeded.Genres.Add("Jazz");
        seeded.Genres.Add("Hard Bop");
        seeded.Composers.Clear();
        seeded.Composers.Add("John Coltrane");
        store.UpsertTrack(seeded, store.Albums[seeded.AlbumId], store.Artists[seeded.ArtistId]);

        // 같은 파일을 다시 열어 값이 살아남았는지 본다.
        var reopened = new CatalogStore(path);
        var again = reopened.Tracks[seeded.Id];

        Assert.Equal(["Jazz", "Hard Bop"], again.Genres);
        Assert.Equal(["John Coltrane"], again.Composers);
    }

    [Fact]
    public void SeedCatalogCarriesGenresAndComposers()
    {
        // 데모 카탈로그도 장르·작곡가를 갖는다 — 없으면 Genres·Composers 화면이 빈다.
        var store = new CatalogStore(TempDb());

        Assert.All(store.Tracks.Values, t => Assert.NotEmpty(t.Genres));
        Assert.All(store.Tracks.Values, t => Assert.NotEmpty(t.Composers));
    }
}
