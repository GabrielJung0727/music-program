using System.Text.Json;
using Mono.Core;
using Mono.Protocol;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 3b 화면들이 기대는 필드가 catalog 응답에 실제로 실리는지 고정한다.
/// Control 은 이 이름들로 역직렬화하므로 Core 가 이름을 바꾸면 화면이 조용히 빈다.
/// </summary>
public class LibraryGroupingTests
{
    private static List<Dictionary<string, JsonElement>> Catalog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var store = new CatalogStore(Path.Combine(dir, "c.db"));
        var json = JsonSerializer.Serialize(store.CatalogView(), LineFraming.JsonOptions);
        return JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json)!;
    }

    private static List<string> ValuesOf(string field) => Catalog()
        .Where(t => t.ContainsKey(field))
        .SelectMany(t => t[field].EnumerateArray().Select(v => v.GetString()!))
        .ToList();

    [Fact]
    public void GenresScreenCanAggregateFromCatalog()
    {
        var genres = ValuesOf("genres").Distinct().ToList();

        Assert.Contains("Jazz", genres);
        Assert.Contains("J-Pop", genres);
    }

    [Fact]
    public void ComposersScreenCanAggregateFromCatalog()
    {
        var composers = ValuesOf("composers").Distinct().ToList();

        Assert.Contains("John Coltrane", composers);
        Assert.Contains("Miles Davis", composers);
    }

    [Fact]
    public void CompositionsScreenCanGroupByWorkKey()
    {
        var works = Catalog()
            .Where(t => t.ContainsKey("workKey"))
            .GroupBy(t => t["workKey"].GetString())
            .ToList();

        Assert.NotEmpty(works);
        Assert.All(works, g => Assert.False(string.IsNullOrEmpty(g.Key)));
    }

    [Fact]
    public void QualityChipsAreNotGenres()
    {
        // Hi-Res·DSD 는 장르가 아니라 품질이다. 장르 목록에 섞이면 안 된다.
        var genres = ValuesOf("genres");

        Assert.DoesNotContain("Hi-Res", genres);
        Assert.DoesNotContain("DSD", genres);
    }
}
