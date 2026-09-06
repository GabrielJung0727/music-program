using System.Text.Json;
using Mono.Core;
using Mono.Protocol;
using Xunit;

namespace Mono.Tests;

/// <summary>앨범 상세는 라이너·크레딧을 담는다 — catalog 응답에는 없는 것들이다.</summary>
public class AlbumDetailTests
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
    public void AlbumDetailCarriesLinerNotesAndTracks()
    {
        var detail = AsDict(NewStore().AlbumDetail("al-kob")!);

        Assert.Equal("Kind of Blue", detail["title"].GetString());
        Assert.False(string.IsNullOrEmpty(detail["linerNotes"].GetString()));
        Assert.True(detail["tracks"].GetArrayLength() >= 2);
    }

    [Fact]
    public void UnknownAlbumIsNull()
    {
        Assert.Null(NewStore().AlbumDetail("al-nope"));
    }

    [Fact]
    public void AlbumTracksUseTheSameShapeAsCatalog()
    {
        var store = NewStore();
        var catalogFields = AsDict(store.CatalogView()[0]).Keys.ToHashSet();

        var track = AsDict(store.AlbumDetail("al-kob")!)["tracks"][0];
        var trackFields = JsonSerializer
            .Deserialize<Dictionary<string, JsonElement>>(track.GetRawText())!.Keys.ToHashSet();

        Assert.Equal(catalogFields, trackFields);
    }
}
