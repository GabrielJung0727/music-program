using Mono.Core;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 방이 생기고 사라지는 규칙.
///
/// 예전에는 지우는 경로가 아예 없어서, 곡을 한 번 틀 때마다 만들어진 방이 Core 가 죽을 때까지
/// 라운지 목록에 남았다. 게다가 혼자 듣기용 방이 HostQueue 로 만들어져 공개 라운지로 보였다.
/// </summary>
public class RoomLifecycleTests
{
    private static RoomManager NewRooms()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return new RoomManager(
            new CatalogStore(Path.Combine(dir, "c.db")),
            new HistoryStore(Path.Combine(dir, "h.db")),
            new StreamingHub(new CatalogStore(Path.Combine(dir, "c2.db"))),
            new EndpointRegistry(Path.Combine(dir, "e.db")));
    }

    /// <summary>혼자 듣는 방은 라운지가 아니다 — 목록에 나가면 유령 라운지가 쌓인다.</summary>
    [Fact]
    public void SoloRoomsStayOutOfTheLoungeDirectory()
    {
        var rooms = NewRooms();
        rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        var lounge = rooms.Create("me", "Live", RoomMode.OpenLounge, "Listener");

        var listed = rooms.ListListed();

        Assert.Equal(2, rooms.List().Count);
        Assert.Single(listed);
        Assert.Equal(lounge.Id, listed[0].Id);
    }

    /// <summary>혼자 듣기에서 탐색까지 막히면 안 된다 — Solo 를 2(HostQueue)로 보내던 시절의 증상이다.</summary>
    [Fact]
    public void SoloPlaybackKeepsSeekingAndTheLocalPath()
    {
        var room = ListeningRoom.ForMode("r", "내 방", "me", RoomMode.Solo);

        Assert.True(room.SeekingAllowed);
        Assert.False(room.IsListed);
        Assert.Equal(PlaybackSourceMode.ClockSync, room.SourceMode);
    }

    /// <summary>호스트가 라운지를 닫아도 혼자 듣기는 남는다 — 방을 지으면 곡이 빈다.</summary>
    [Fact]
    public void ClosingALoungeReturnsItToSolo()
    {
        var rooms = NewRooms();
        var room = rooms.Create("host", "Live", RoomMode.OpenLounge, "Host");
        rooms.Join(room.Id, "guest", PeerRole.Control, null, "Guest");
        room.Queue.Add(new QueueItem { Id = "q1", TrackId = "t1", AddedByPeerId = "host" });
        room.Playing = true;

        var (closed, error) = rooms.Close(room.Id, "host");

        Assert.Null(error);
        Assert.NotNull(closed);
        Assert.Equal(RoomMode.Solo, closed!.Mode);
        Assert.False(closed.IsListed);
        Assert.True(closed.Playing);
        Assert.Single(closed.Queue);
        Assert.Contains("host", closed.ControlPeerIds);
        Assert.DoesNotContain("guest", closed.ControlPeerIds);
        Assert.Single(rooms.List());
        Assert.Empty(rooms.ListListed());
    }

    /// <summary>호스트가 아니면 남의 라운지를 닫을 수 없다.</summary>
    [Fact]
    public void OnlyTheHostCanCloseARoom()
    {
        var rooms = NewRooms();
        var room = rooms.Create("host", "Live", RoomMode.OpenLounge, "Host");

        var (closed, error) = rooms.Close(room.Id, "someone-else");

        Assert.Null(closed);
        Assert.NotNull(error);
        Assert.Single(rooms.List());
    }

    /// <summary>
    /// 창을 닫거나 새로고침하면 leave_room 은 오지 않는다. 그때도 방에서 빠져야 한다 —
    /// 안 그러면 끊긴 사람이 멤버로 남아 방이 영영 비지 않고, 청소부가 있어도 지울 수 없다.
    /// </summary>
    [Fact]
    public void DroppingTheConnectionLeavesTheRoom()
    {
        var rooms = NewRooms();
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        Assert.True(room.HasMembers);

        rooms.DetachControl("me");

        Assert.False(room.HasMembers);
    }

    /// <summary>비어 있어도 유예 시간 안에는 남는다 — 새로고침 한 번에 큐를 잃으면 안 된다.</summary>
    [Fact]
    public void AnEmptyRoomSurvivesTheGracePeriod()
    {
        var rooms = NewRooms();
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.DetachControl("me");
        var now = DateTimeOffset.UtcNow;

        Assert.Empty(rooms.SweepEmpty(TimeSpan.FromMinutes(2), now));
        Assert.Empty(rooms.SweepEmpty(TimeSpan.FromMinutes(2), now.AddMinutes(1)));
        Assert.Single(rooms.List());
        Assert.Equal(room.Id, rooms.List()[0].Id);
    }

    /// <summary>유예가 지나면 지운다. 이게 없어서 방이 무한히 쌓였다.</summary>
    [Fact]
    public void AnEmptyRoomIsSweptOnceTheGracePasses()
    {
        var rooms = NewRooms();
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.DetachControl("me");
        var now = DateTimeOffset.UtcNow;

        rooms.SweepEmpty(TimeSpan.FromMinutes(2), now);
        var removed = rooms.SweepEmpty(TimeSpan.FromMinutes(2), now.AddMinutes(3));

        Assert.Equal([room.Id], removed);
        Assert.Empty(rooms.List());
    }

    /// <summary>사람이 있는 방은 아무리 오래 돼도 건드리지 않는다.</summary>
    [Fact]
    public void OccupiedRoomsAreNeverSwept()
    {
        var rooms = NewRooms();
        rooms.Create("host", "Live", RoomMode.OpenLounge, "Host");   // 호스트는 만든 순간부터 멤버다
        var now = DateTimeOffset.UtcNow;

        rooms.SweepEmpty(TimeSpan.FromMinutes(2), now);
        var removed = rooms.SweepEmpty(TimeSpan.FromMinutes(2), now.AddHours(9));

        Assert.Empty(removed);
        Assert.Single(rooms.List());
    }

    /// <summary>비었다가 다시 사람이 들어오면 유예 시계는 처음으로 돌아간다.</summary>
    [Fact]
    public void ComingBackResetsTheGraceClock()
    {
        var rooms = NewRooms();
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.DetachControl("me");
        var now = DateTimeOffset.UtcNow;

        rooms.SweepEmpty(TimeSpan.FromMinutes(2), now);
        room.ControlPeerIds.Add("me");
        rooms.SweepEmpty(TimeSpan.FromMinutes(2), now.AddMinutes(5));   // 사람이 있다 → 시계 초기화
        room.ControlPeerIds.Remove("me");
        var removed = rooms.SweepEmpty(TimeSpan.FromMinutes(2), now.AddMinutes(6));

        Assert.Empty(removed);
        Assert.Single(rooms.List());
    }
}
