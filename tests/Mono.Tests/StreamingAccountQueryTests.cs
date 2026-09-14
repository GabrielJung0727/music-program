using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using System.Text.Json;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// Control UI 는 켜질 때 "지금 어떤 스트리밍 계정이 붙어 있나"를 물어야 한다.
/// 그 질문이 없어서 Core 가 Tidal 에 연동돼 있어도 화면은 계속 "연결 안 됨"으로 보였고,
/// 사용자가 연결 버튼을 눌러도 Core 가 "이미 연동됨"이라 답해 아무 일도 일어나지 않았다.
///
/// link_streaming 은 토큰 없이 보내면 연결을 <b>끊어 버리므로</b> 조회용으로 쓸 수 없다.
/// 그래서 부작용 없는 streaming_accounts 질의가 따로 필요하다.
/// </summary>
public class StreamingAccountQueryTests
{
    private static (CommandProcessor Commands, StreamingHub Streaming) NewStack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var history = new HistoryStore(Path.Combine(dir, "h.db"));
        var streaming = new StreamingHub(catalog, Path.Combine(dir, "streaming.json"));
        var endpoints = new EndpointRegistry(Path.Combine(dir, "e.db"));
        var zones = new ZoneRegistry(Path.Combine(dir, "z.db"));
        var rooms = new RoomManager(catalog, history, streaming, endpoints);
        var scanner = new LibraryScanner(catalog, new ArtworkService(Path.Combine(dir, "art")));
        var pairing = new PairingService();
        var wiki = new WikipediaService(new HttpClient(), Path.Combine(dir, "wiki"));
        var backups = new BackupService(dir);
        var commands = new CommandProcessor(
            rooms, catalog, scanner, streaming, pairing, history, endpoints, zones, wiki, backups,
            [Path.Combine(dir, "library")]);
        return (commands, streaming);
    }

    private static List<AccountView> Accounts(MonoMessage msg)
    {
        Assert.NotNull(msg.Body);
        return JsonSerializer.Deserialize<List<AccountView>>(msg.Body!, LineFraming.JsonOptions)!;
    }

    private sealed record AccountView(
        StreamingProvider Provider,
        bool Connected,
        string? DisplayName,
        bool LiveSdk,
        int Imported,
        string? Note,
        string? AuthMode);

    [Fact]
    public void StreamingAccountsReportsConnectionWithoutChangingIt()
    {
        var (commands, streaming) = NewStack();
        streaming.Link(StreamingProvider.Tidal, "token-abc", "TIDAL");
        Assert.True(streaming.IsConnected(StreamingProvider.Tidal));

        var result = commands.Execute("peer", new MonoMessage { Type = MessageTypes.StreamingAccounts }, "Listener");

        Assert.NotNull(result.Direct);
        Assert.Equal(MessageTypes.StreamingAccounts, result.Direct!.Type);
        var tidal = Accounts(result.Direct).Single(a => a.Provider == StreamingProvider.Tidal);
        Assert.True(tidal.Connected);

        // 핵심: 조회가 연결을 건드리면 안 된다.
        Assert.True(streaming.IsConnected(StreamingProvider.Tidal));
    }

    [Fact]
    public void StreamingAccountsIsSafeToCallWhenNothingIsLinked()
    {
        var (commands, _) = NewStack();

        var result = commands.Execute("peer", new MonoMessage { Type = MessageTypes.StreamingAccounts }, "Listener");

        Assert.NotNull(result.Direct);
        Assert.All(Accounts(result.Direct!), a => Assert.False(a.Connected));
    }

    /// <summary>
    /// 토큰 없는 link_streaming 이 연결을 끊는다는 사실을 못 박아 둔다 —
    /// 이것 때문에 조회용으로 재사용할 수 없다.
    /// </summary>
    [Fact]
    public void LinkStreamingWithoutTokenStillUnlinks()
    {
        var (commands, streaming) = NewStack();
        streaming.Link(StreamingProvider.Tidal, "token-abc", "TIDAL");

        commands.Execute("peer", new MonoMessage
        {
            Type = MessageTypes.LinkStreaming,
            Provider = StreamingProvider.Tidal
        }, "Listener");

        Assert.False(streaming.IsConnected(StreamingProvider.Tidal));
    }
}
