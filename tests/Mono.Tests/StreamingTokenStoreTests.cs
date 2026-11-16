using Mono.Core;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 스트리밍 토큰은 streaming.json 에 남는다. 평문이면 파일이 백업·동기화에 섞여 나가거나
/// 다른 사람에게 건네질 때 계정이 같이 넘어간다. Windows 에서는 이 사용자만 풀 수 있게 감싼다.
/// 같은 사용자로 도는 프로그램은 막지 못한다 — 그건 DPAPI 의 한계다.
/// </summary>
public class StreamingTokenStoreTests
{
    // 실제 토큰처럼 보이지 않게 짧게 둔다. 진짜처럼 보이면 StreamingHub 가 TIDAL 에 물으러 간다.
    private const string Token = "tok-7f3a9c";

    private static (CatalogStore Catalog, string Path) NewStore()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return (new CatalogStore(System.IO.Path.Combine(dir, "c.db")), System.IO.Path.Combine(dir, "streaming.json"));
    }

    [Fact]
    public void TheSavedFileDoesNotHoldTheTokenInPlainText()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI 는 Windows 에만 있다
        var (catalog, path) = NewStore();

        new StreamingHub(catalog, path).Link(StreamingProvider.Tidal, Token, "me");

        Assert.DoesNotContain(Token, File.ReadAllText(path));
    }

    [Fact]
    public void ASavedAccountComesBackOnTheNextStart()
    {
        var (catalog, path) = NewStore();
        new StreamingHub(catalog, path).Link(StreamingProvider.Tidal, Token, "me");

        var reopened = new StreamingHub(catalog, path);

        Assert.True(reopened.IsConnected(StreamingProvider.Tidal));
    }

    [Fact]
    public void APlainFileFromAnOlderVersionIsReadThenRewrittenWrapped()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI 는 Windows 에만 있다
        var (catalog, path) = NewStore();
        File.WriteAllText(path,
            $$"""{"country":"KR","accounts":[{"provider":"Tidal","token":"{{Token}}","refreshToken":null,"expiresAt":null,"displayName":"me","country":"KR"}]}""");

        var hub = new StreamingHub(catalog, path);

        Assert.True(hub.IsConnected(StreamingProvider.Tidal));
        Assert.DoesNotContain(Token, File.ReadAllText(path));
        Assert.True(new StreamingHub(catalog, path).IsConnected(StreamingProvider.Tidal));
    }

    [Fact]
    public void AFileThatCannotBeUnwrappedMeansSigningInAgainNotACrash()
    {
        // 백업을 다른 PC 나 다른 사용자로 되살린 경우다. 풀 수 없으면 연결이 없는 상태로 뜬다.
        var (catalog, path) = NewStore();
        File.WriteAllText(path, """{"format":"mono-dpapi-v1","data":"AAECAwQFBgcICQ=="}""");

        var hub = new StreamingHub(catalog, path);

        Assert.False(hub.IsConnected(StreamingProvider.Tidal));
    }
}
