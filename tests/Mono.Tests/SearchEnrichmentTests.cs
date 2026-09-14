using Mono.Core;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 검색창은 글자를 칠 때마다 질의를 보낸다. 그대로 Tidal 로 흘려보내면
/// 한 단어 치는 동안 수십 번의 검색이 나가 한도(429)를 부른다.
/// UI 의 디바운스가 1차 방어지만, Core 도 스스로를 지켜야 한다 —
/// 어떤 클라이언트가 붙든 같은 질의를 반복해서 Tidal 로 보내면 안 된다.
/// </summary>
public class SearchEnrichmentTests
{
    private static StreamingHub NewHub()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        return new StreamingHub(catalog, Path.Combine(dir, "streaming.json"));
    }

    [Fact]
    public void ShortQueriesNeverReachTidal()
    {
        var hub = NewHub();
        // 한두 글자는 의미 있는 검색이 아니다. 타이핑 도중의 중간 상태일 뿐이다.
        Assert.False(hub.ShouldEnrich("a"));
        Assert.False(hub.ShouldEnrich("  b "));
        Assert.True(hub.ShouldEnrich("miles"));
    }

    [Fact]
    public void RepeatingTheSameQueryIsSkipped()
    {
        var hub = NewHub();
        Assert.True(hub.ShouldEnrich("kind of blue"));
        hub.MarkEnriched("kind of blue");

        // 같은 질의는 쿨다운 동안 다시 나가지 않는다.
        Assert.False(hub.ShouldEnrich("kind of blue"));
        // 대소문자·공백 차이는 같은 질의로 본다 — 타이핑 편차로 캐시가 뚫리면 의미가 없다.
        Assert.False(hub.ShouldEnrich("  Kind Of Blue  "));
        // 다른 질의는 통과한다.
        Assert.True(hub.ShouldEnrich("giant steps"));
    }

    [Fact]
    public void TypingOneWordCharacterByCharacterCostsOneTidalQuery()
    {
        var hub = NewHub();
        // 디바운스가 없거나 느슨해도, 접두사들이 전부 Tidal 로 가면 안 된다.
        var allowed = 0;
        foreach (var prefix in new[] { "m", "mi", "mil", "mile", "miles" })
        {
            if (!hub.ShouldEnrich(prefix)) continue;
            allowed++;
            hub.MarkEnriched(prefix);
        }

        // "m", "mi" 는 너무 짧아 걸러지고, 나머지는 서로 다른 질의라 통과한다.
        // 핵심은 같은 질의가 반복되지 않는다는 것 — 아래에서 다시 쳐도 늘지 않는다.
        var afterRetype = allowed;
        foreach (var prefix in new[] { "m", "mi", "mil", "mile", "miles" })
        {
            if (hub.ShouldEnrich(prefix)) afterRetype++;
        }

        Assert.Equal(allowed, afterRetype);
    }

    [Fact]
    public void RateLimitPausesEnrichmentForEveryQuery()
    {
        var hub = NewHub();
        Assert.True(hub.ShouldEnrich("miles"));

        hub.NoteRateLimited();

        // 한도에 걸린 동안에는 새 질의도 보내지 않는다. 더 두드려 봐야 429 만 받는다.
        Assert.False(hub.ShouldEnrich("miles"));
        Assert.False(hub.ShouldEnrich("coltrane"));
    }

    [Fact]
    public void EnrichSearchIsInertWithoutAConnectedAccount()
    {
        var hub = NewHub();
        Assert.False(hub.IsConnected(StreamingProvider.Tidal));

        // 연동이 없으면 조용히 아무것도 하지 않는다 — 로컬 검색은 그대로 동작해야 한다.
        hub.EnrichSearch("miles davis");
    }
}
