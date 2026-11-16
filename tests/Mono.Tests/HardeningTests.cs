using System.Text;
using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// Core 에는 로그인이 없다. 그래서 "누가 말을 거는가"를 요청과 연결에서 가려내는 규칙들이
/// 사실상의 인증이다. 여기 있는 테스트는 그 규칙이 조용히 풀리지 않게 붙잡아 둔다.
/// </summary>
public class HardeningTests
{
    // ── 요청 거르기 ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("[::1]", true)]
    [InlineData("192.168.0.10", true)]
    [InlineData("attacker.example", false)]   // DNS 리바인딩은 도메인 이름으로 들어온다
    [InlineData("", false)]
    public void HostMustBeAnAddressOrLocalhost(string host, bool allowed)
        => Assert.Equal(allowed, RequestGuard.IsAllowedHost(host, []));

    [Fact]
    public void ANamedHostIsAdmittedOnlyWhenListed()
    {
        Assert.False(RequestGuard.IsAllowedHost("mono.lan", []));
        Assert.True(RequestGuard.IsAllowedHost("mono.lan", ["mono.lan"]));
    }

    [Theory]
    [InlineData(null, true)]                         // 네이티브 클라이언트, 같은 출처의 GET
    [InlineData("http://127.0.0.1:7702", true)]
    [InlineData("http://localhost:5273", true)]      // Vite 개발 서버
    [InlineData("https://attacker.example", false)]  // 방문한 아무 사이트
    [InlineData("null", false)]                      // 샌드박스 iframe, file://
    public void OnlyThisMachinesPagesMayCallCore(string? origin, bool allowed)
        => Assert.Equal(allowed, RequestGuard.IsAllowedOrigin(origin, "127.0.0.1", 7702));

    [Fact]
    public void APageServedFromTheSameLanAddressIsSameOrigin()
    {
        Assert.True(RequestGuard.IsAllowedOrigin("http://192.168.0.10:7702", "192.168.0.10", 7702));
        Assert.False(RequestGuard.IsAllowedOrigin("http://192.168.0.10:8080", "192.168.0.10", 7702));
        Assert.False(RequestGuard.IsAllowedOrigin("http://192.168.0.99:7702", "192.168.0.10", 7702));
    }

    [Theory]
    [InlineData("GET", "/", true)]
    [InlineData("GET", "/assets/app.js", true)]
    [InlineData("GET", "/ws/control", true)]         // 페어링 토큰은 hello 에서 따로 확인한다
    [InlineData("GET", "/api/art/tr-1", true)]
    [InlineData("GET", "/api/health", true)]
    [InlineData("GET", "/api/setup", false)]
    [InlineData("PUT", "/api/setup", false)]
    [InlineData("DELETE", "/api/setup", false)]
    [InlineData("GET", "/api/rooms/abc", false)]     // 초대 코드와 채팅이 실려 있다
    [InlineData("GET", "/hub", false)]
    [InlineData("POST", "/api/art/tr-1", false)]
    public void RemoteMachinesGetOnlyTheReadOnlySurface(string method, string path, bool open)
        => Assert.Equal(open, RequestGuard.IsOpenToRemote(method, path));

    // ── peer id ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 방의 권한은 전부 peer id 비교이고, 호스트의 id 는 스냅샷으로 모두에게 보인다.
    /// id 를 적어 보내는 것만으로 그 사람이 될 수 있으면 안 된다.
    /// </summary>
    [Fact]
    public void AKnownPeerIdCannotBeTakenWithoutItsSecret()
    {
        var connections = new ConnectionRegistry();
        var (id, secret) = connections.Claim("host", null);
        Assert.Equal("host", id);

        Assert.NotEqual("host", connections.Claim("host", null).PeerId);
        Assert.NotEqual("host", connections.Claim("host", "wrong").PeerId);

        var back = connections.Claim("host", secret);
        Assert.Equal("host", back.PeerId);
        Assert.Equal(secret, back.Secret);
    }

    /// <summary>
    /// 새로 고침 직후에는 같은 id 의 새 연결이 먼저 붙고, 옛 소켓의 정리 코드가 나중에 돈다.
    /// 그 정리가 새 연결의 등록을 지우면 화면은 멀쩡한데 방송이 오지 않는다.
    /// </summary>
    [Fact]
    public void AnOldConnectionCannotUnregisterTheOneThatReplacedIt()
    {
        var connections = new ConnectionRegistry();
        var oldCalls = 0;
        var newCalls = 0;
        Func<MonoMessage, Task> older = _ => { oldCalls++; return Task.CompletedTask; };
        Func<MonoMessage, Task> newer = _ => { newCalls++; return Task.CompletedTask; };

        connections.Controls["p"] = older;
        connections.Controls["p"] = newer;

        Assert.False(connections.ReleaseControl("p", older));
        Assert.True(connections.Controls.ContainsKey("p"));
        Assert.True(connections.ReleaseControl("p", newer));
        Assert.Equal(0, oldCalls + newCalls);
    }

    // ── 페어링 ──────────────────────────────────────────────────────────────

    [Fact]
    public void AValidPairingCodeWorksExactlyOnce()
    {
        var pairing = new PairingService();
        var code = pairing.Issue("owner").Code;

        var first = pairing.Redeem(code);
        Assert.NotNull(first.SessionToken);
        Assert.True(pairing.IsAuthorized(first.SessionToken));
        Assert.Null(pairing.Redeem(code).SessionToken);
    }

    /// <summary>6자리 코드는 대입으로 뚫린다. 연달아 틀리면 잠그고, 나와 있던 코드도 버린다.</summary>
    [Fact]
    public void RepeatedWrongCodesLockRedeemAndVoidOutstandingCodes()
    {
        var pairing = new PairingService();
        var code = pairing.Issue("owner").Code;

        // 발급되는 코드는 100000 이상이라 "000000" 은 절대 맞지 않는다.
        for (var i = 0; i < 8; i++) Assert.Null(pairing.Redeem("000000").SessionToken);

        Assert.Null(pairing.Redeem(code).SessionToken);
    }

    // ── 줄 읽기 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LinesAreSplitOnNewlinesAndEndAtEof()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{\"a\":1}\r\n한글\nlast")).ToArray();
        var reader = new BoundedLineReader(new MemoryStream(bytes), 1024);

        Assert.Equal("{\"a\":1}", await reader.ReadLineAsync(default));   // BOM 과 \r 은 떼어 낸다
        Assert.Equal("한글", await reader.ReadLineAsync(default));
        Assert.Equal("last", await reader.ReadLineAsync(default));
        Assert.Null(await reader.ReadLineAsync(default));
    }

    [Fact]
    public async Task ALineOverTheLimitEndsTheConnection()
    {
        var reader = new BoundedLineReader(new MemoryStream(new byte[100_000]), 4096);

        await Assert.ThrowsAsync<IOException>(async () => await reader.ReadLineAsync(default));
    }

    // ── 명령 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 페어링으로 들어온 원격 사용자는 라운지의 손님이다. 이 PC 의 폴더를 훑거나,
    /// 스트리밍 계정을 바꾸거나, 접속 코드를 더 찍어 낼 수는 없다.
    /// </summary>
    [Theory]
    [InlineData(MessageTypes.ScanLibrary)]
    [InlineData(MessageTypes.Backup)]
    [InlineData(MessageTypes.LinkStreaming)]
    [InlineData(MessageTypes.Pair)]
    [InlineData(MessageTypes.Folders)]
    [InlineData(MessageTypes.ExportM3u)]
    [InlineData(MessageTypes.ZoneRemoveMember)]
    public void RemoteConnectionsCannotRunOwnerCommands(string type)
    {
        var (commands, _) = NewStack();

        var result = commands.Execute("guest", new MonoMessage { Type = type, Path = "C:\\" }, "Guest", local: false);

        Assert.Equal(MessageTypes.Error, result.Direct?.Type);
        Assert.Null(result.BroadcastRoom);
    }

    /// <summary>방 id 를 아는 것과 그 방에 들어와 있는 것은 다르다.</summary>
    [Fact]
    public void AStrangerCannotChatInARoomTheyHaveNotJoined()
    {
        var (commands, rooms) = NewStack();
        var room = rooms.Create("host", "Friends", RoomMode.Invite, "Host");

        var result = commands.Execute("stranger", new MonoMessage { Type = MessageTypes.Chat, RoomId = room.Id, Text = "hi" }, "Stranger");

        Assert.Empty(room.Chat);
        Assert.Equal(MessageTypes.Error, result.Direct?.Type);
    }

    [Fact]
    public void ASoloRoomCannotBeJoinedByItsId()
    {
        var (_, rooms) = NewStack();
        var solo = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");

        var joined = rooms.Join(solo.Id, "someone-else", PeerRole.Control, null, "Other");

        Assert.Null(joined.Room);
        Assert.DoesNotContain("someone-else", solo.ControlPeerIds);
    }

    /// <summary>
    /// 화면은 "끄기"로 현재 프리셋과 flag=false 를 보낸다. flag 를 보지 않던 동안에는
    /// 그 명령으로 DSP 가 꺼지지 않았고, 비트 퍼펙트 표시도 돌아오지 않았다.
    /// </summary>
    [Fact]
    public void TurningDspOffWithTheFlagClearsThePreset()
    {
        var (commands, rooms) = NewStack();
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");

        commands.Execute("me", new MonoMessage { Type = MessageTypes.SetDsp, RoomId = room.Id, Dsp = DspPresetKind.Headphones, Flag = true }, "Listener");
        Assert.True(room.DspEnabled);

        commands.Execute("me", new MonoMessage { Type = MessageTypes.SetDsp, RoomId = room.Id, Dsp = DspPresetKind.Headphones, Flag = false }, "Listener");
        Assert.False(room.DspEnabled);
        Assert.Equal(DspPresetKind.Off, room.DspPreset);
    }

    [Fact]
    public void ChatLinesAreClipped()
    {
        var (commands, rooms) = NewStack();
        var room = rooms.Create("host", "Live", RoomMode.OpenLounge, "Host");

        commands.Execute("host", new MonoMessage { Type = MessageTypes.Chat, RoomId = room.Id, Text = new string('x', 50_000) }, "Host");

        Assert.Equal(CommandProcessor.MaxChatLength, Assert.Single(room.Chat).Text.Length);
    }

    private static (CommandProcessor Commands, RoomManager Rooms) NewStack()
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
        var commands = new CommandProcessor(
            rooms, catalog, scanner, streaming, new PairingService(), history, endpoints, zones,
            new WikipediaService(new HttpClient(), Path.Combine(dir, "wiki")), new BackupService(dir),
            [Path.Combine(dir, "library")]);
        return (commands, rooms);
    }
}
