using System.Text.Json;
using Mono.Core;
using Mono.Protocol;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// Control 은 catalog 와 search 응답을 같은 파서로 읽는다.
/// 두 응답의 모양이 어긋나면 검색 결과에서 아티스트·아트·배지가 사라진다.
/// </summary>
public class CatalogViewTests
{
    private static CatalogStore NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return new CatalogStore(Path.Combine(dir, "c.db"));
    }

    private static Dictionary<string, JsonElement> AsDict(object item)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(item, LineFraming.JsonOptions))!;

    [Fact]
    public void SearchReturnsTheSameShapeAsCatalog()
    {
        var store = NewStore();

        var catalogFields = AsDict(store.CatalogView()[0]).Keys.ToHashSet();
        var searchFields = AsDict(store.Search("")[0]).Keys.ToHashSet();

        Assert.Equal(catalogFields, searchFields);
    }

    [Fact]
    public void SearchStillMatchesArtistAliases()
    {
        var store = NewStore();
        var artist = store.Artists.Values.First(a => a.AlternateNames.Count > 0);

        var hits = store.Search(artist.AlternateNames[0]);

        Assert.NotEmpty(hits);
    }

    [Fact]
    public void SearchDoesNotLeakLocalPaths()
    {
        // 뷰 모양으로 통일되면 파일 경로는 hasLocal 불리언으로만 나간다.
        var fields = AsDict(NewStore().Search("")[0]).Keys.ToHashSet();

        Assert.DoesNotContain("localPath", fields);
        Assert.Contains("hasLocal", fields);
    }

    [Fact]
    public void SearchMatchesGenreAndComposer()
    {
        var store = NewStore();

        Assert.NotEmpty(store.Search("Hard Bop"));
        Assert.NotEmpty(store.Search("Coltrane"));
    }

    [Fact]
    public void CatalogViewCarriesGenresComposersAndWork()
    {
        var store = NewStore();
        var track = store.Tracks["tr-blue-train"];
        track.Title = "Symphony No. 5 in C minor, Op. 67: I. Allegro con brio";
        track.Composers.Clear();
        track.Composers.Add("Ludwig van Beethoven");
        store.UpsertTrack(track, store.Albums[track.AlbumId], store.Artists[track.ArtistId]);

        var item = AsDict(store.CatalogView().Single(v => AsDict(v)["id"].GetString() == "tr-blue-train"));

        Assert.Equal("Jazz", item["genres"][0].GetString());
        Assert.Equal("Ludwig van Beethoven", item["composers"][0].GetString());
        Assert.Equal("Symphony No. 5 in C minor, Op. 67", item["workTitle"].GetString());
        Assert.False(string.IsNullOrEmpty(item["workKey"].GetString()));
    }

    [Fact]
    public void TracksWithoutComposerHaveNoWork()
    {
        var store = NewStore();
        var track = store.Tracks["tr-so-what"];
        track.Composers.Clear();
        store.UpsertTrack(track, store.Albums[track.AlbumId], store.Artists[track.ArtistId]);

        var item = AsDict(store.CatalogView().Single(v => AsDict(v)["id"].GetString() == "tr-so-what"));

        // JsonOptions 가 WhenWritingNull 이라 null 필드는 아예 실리지 않는다 — 프로토콜 전반의 관례다.
        // Control 쪽에서는 없는 필드가 null 로 역직렬화되므로 결과는 같다.
        Assert.False(item.ContainsKey("workKey"));
        Assert.False(item.ContainsKey("workTitle"));
    }
}
