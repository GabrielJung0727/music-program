using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using System.Text.Json;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 곡을 트는 것과 라운지를 여는 것은 다른 일이다.
///
/// 예전에는 재생이 곧 공개 라운지였다 — 웹이 Solo 를 2(=HostQueue)로 보내는 바람에
/// 혼자 듣기용 방이 진짜 라운지로 만들어졌고, 목록에 그대로 실렸다. 게다가 닫는 경로가
/// 없어서 한 번 생긴 방은 Core 가 죽을 때까지 남았다.
/// </summary>
public class RoomCommandTests
{
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

    private static List<JsonElement> Listed(CommandProcessor commands, string peer)
    {
        var result = commands.Execute(peer, new MonoMessage { Type = MessageTypes.ListRooms }, "Listener");
        Assert.NotNull(result.Direct?.Body);
        return JsonSerializer.Deserialize<List<JsonElement>>(result.Direct!.Body!, LineFraming.JsonOptions)!;
    }

    /// <summary>혼자 듣기용 방을 만들어도 라운지 목록에는 아무것도 뜨지 않는다.</summary>
    [Fact]
    public void PlayingAloneDoesNotOpenALounge()
    {
        var (commands, rooms) = NewStack();

        commands.Execute("me", new MonoMessage
        {
            Type = MessageTypes.CreateRoom,
            RoomName = "내 방",
            Mode = RoomMode.Solo,
        }, "Listener");

        Assert.Single(rooms.List());
        Assert.Empty(Listed(commands, "me"));
    }

    /// <summary>
    /// 호스트를 고르면 듣던 방이 그 자리에서 라운지가 된다 — 새 방을 만들지 않으므로
    /// 큐도 재생 위치도 그대로다.
    /// </summary>
    [Fact]
    public void PublishingTurnsTheSoloRoomIntoTheLounge()
    {
        var (commands, rooms) = NewStack();
        var solo = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        solo.Queue.Add(new QueueItem { Id = "q1", TrackId = "t1", AddedByPeerId = "me" });
        solo.QueueIndex = 3;
        solo.Playing = true;

        commands.Execute("me", new MonoMessage
        {
            Type = MessageTypes.PublishRoom,
            RoomId = solo.Id,
            RoomName = "Listener Live",
            Mode = RoomMode.OpenLounge,
        }, "Listener");

        Assert.Single(rooms.List());                       // 방이 하나 더 생기지 않았다
        var listed = Listed(commands, "me");
        Assert.Single(listed);
        Assert.Equal(solo.Id, listed[0].GetProperty("id").GetString());
        Assert.Equal("Listener Live", solo.Name);
        Assert.Equal(RoomMode.OpenLounge, solo.Mode);
        Assert.Single(solo.Queue);                          // 듣던 큐가 살아 있다
        Assert.Equal(3, solo.QueueIndex);
        Assert.True(solo.Playing);
    }

    /// <summary>비공개로 열면 초대 코드가 붙는다.</summary>
    [Fact]
    public void PublishingUnlistedIssuesAnInviteCode()
    {
        var (commands, rooms) = NewStack();
        var solo = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");

        commands.Execute("me", new MonoMessage
        {
            Type = MessageTypes.PublishRoom,
            RoomId = solo.Id,
            RoomName = "Quiet",
            Mode = RoomMode.Invite,
        }, "Listener");

        Assert.Equal(RoomMode.Invite, solo.Mode);
        Assert.False(string.IsNullOrWhiteSpace(solo.InviteCode));
        Assert.NotNull(solo.InviteExpiresAt);
    }

    /// <summary>세션을 끝내면 방이 사라진다. 나가기만 하면 호스트 없는 방이 목록에 남는다.</summary>
    [Fact]
    public void ClosingTheLoungeRemovesItFromTheDirectory()
    {
        var (commands, rooms) = NewStack();
        var room = rooms.Create("host", "Live", RoomMode.OpenLounge, "Host");
        Assert.Single(Listed(commands, "host"));

        commands.Execute("host", new MonoMessage { Type = MessageTypes.CloseRoom, RoomId = room.Id }, "Host");

        Assert.Empty(rooms.List());
        Assert.Empty(Listed(commands, "host"));
    }

    /// <summary>호스트가 아니면 닫을 수 없다 — 남의 라운지를 지워 버리면 안 된다.</summary>
    [Fact]
    public void AGuestCannotCloseSomeoneElsesLounge()
    {
        var (commands, rooms) = NewStack();
        var room = rooms.Create("host", "Live", RoomMode.OpenLounge, "Host");

        var result = commands.Execute("guest", new MonoMessage { Type = MessageTypes.CloseRoom, RoomId = room.Id }, "Guest");

        Assert.Single(rooms.List());
        Assert.NotNull(result.Direct);
        Assert.Equal(MessageTypes.Error, result.Direct!.Type);
    }

    /// <summary>
    /// 한 사람은 라운지를 하나만 연다. 여러 개를 열 수 있으면 같은 호스트의 방이 목록을
    /// 채우고, 듣는 쪽에서는 어느 게 진짜인지 알 수 없다.
    /// </summary>
    [Fact]
    public void OnePersonCanOnlyOpenOneLounge()
    {
        var (commands, rooms) = NewStack();
        commands.Execute("me", new MonoMessage
        {
            Type = MessageTypes.CreateRoom, RoomName = "첫 번째", Mode = RoomMode.OpenLounge,
        }, "Listener");

        var second = commands.Execute("me", new MonoMessage
        {
            Type = MessageTypes.CreateRoom, RoomName = "두 번째", Mode = RoomMode.OpenLounge,
        }, "Listener");

        Assert.Equal(MessageTypes.Error, second.Direct?.Type);
        Assert.Contains("첫 번째", second.Direct!.Error);
        Assert.Single(rooms.List());
    }

    /// <summary>혼자 듣기는 라운지가 아니다 — 라운지를 열어 둔 채로도 혼자 들을 수 있어야 한다.</summary>
    [Fact]
    public void ASoloRoomIsNotBlockedByAnOpenLounge()
    {
        var (commands, rooms) = NewStack();
        commands.Execute("me", new MonoMessage
        {
            Type = MessageTypes.CreateRoom, RoomName = "Live", Mode = RoomMode.OpenLounge,
        }, "Listener");

        var solo = commands.Execute("me", new MonoMessage
        {
            Type = MessageTypes.CreateRoom, RoomName = "내 방", Mode = RoomMode.Solo,
        }, "Listener");

        Assert.NotEqual(MessageTypes.Error, solo.Direct?.Type ?? "");
        Assert.Equal(2, rooms.List().Count);
        Assert.Single(Listed(commands, "me"));
    }

    /// <summary>다른 사람은 각자 하나씩 열 수 있다.</summary>
    [Fact]
    public void TheLimitIsPerPersonNotGlobal()
    {
        var (commands, rooms) = NewStack();
        commands.Execute("a", new MonoMessage { Type = MessageTypes.CreateRoom, RoomName = "A", Mode = RoomMode.OpenLounge }, "A");
        var b = commands.Execute("b", new MonoMessage { Type = MessageTypes.CreateRoom, RoomName = "B", Mode = RoomMode.OpenLounge }, "B");

        Assert.NotEqual(MessageTypes.Error, b.Direct?.Type ?? "");
        Assert.Equal(2, Listed(commands, "a").Count);
    }

    /// <summary>닫고 나면 다시 열 수 있다.</summary>
    [Fact]
    public void ClosingFreesTheSlot()
    {
        var (commands, rooms) = NewStack();
        var first = rooms.Create("me", "첫 번째", RoomMode.OpenLounge, "Listener");
        commands.Execute("me", new MonoMessage { Type = MessageTypes.CloseRoom, RoomId = first.Id }, "Listener");

        var again = commands.Execute("me", new MonoMessage
        {
            Type = MessageTypes.CreateRoom, RoomName = "두 번째", Mode = RoomMode.OpenLounge,
        }, "Listener");

        Assert.NotEqual(MessageTypes.Error, again.Direct?.Type ?? "");
        Assert.Single(rooms.List());
    }

    /// <summary>이미 라운지가 열려 있으면 듣던 방을 또 공개로 올릴 수 없다.</summary>
    [Fact]
    public void PublishingASecondLoungeIsRefused()
    {
        var (commands, rooms) = NewStack();
        rooms.Create("me", "Live", RoomMode.OpenLounge, "Listener");
        var solo = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");

        var result = commands.Execute("me", new MonoMessage
        {
            Type = MessageTypes.PublishRoom, RoomId = solo.Id, RoomName = "또 하나", Mode = RoomMode.OpenLounge,
        }, "Listener");

        Assert.Equal(MessageTypes.Error, result.Direct?.Type);
        Assert.Equal(RoomMode.Solo, solo.Mode);
        Assert.Single(Listed(commands, "me"));
    }
}
