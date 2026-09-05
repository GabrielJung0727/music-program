using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// Core가 내보내는 룸 스냅샷과 Control이 읽는 DTO의 계약을 고정한다.
/// Core가 필드를 바꾸거나 없애면 여기서 깨져야 한다.
/// </summary>
public class SnapshotContractTests
{
    private static (RoomManager Rooms, CatalogStore Catalog) NewStack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var history = new HistoryStore(Path.Combine(dir, "h.db"));
        var streaming = new StreamingHub(catalog);
        var endpoints = new EndpointRegistry(Path.Combine(dir, "e.db"));
        return (new RoomManager(catalog, history, streaming, endpoints), catalog);
    }

    [Fact]
    public void ScalarsSurviveRoundTrip()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Night Lounge", RoomMode.Audiophile, "Host");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room));

        Assert.NotNull(snap);
        Assert.Equal(room.Id, snap!.Id);
        Assert.Equal("Night Lounge", snap.Name);
        Assert.Equal(RoomMode.Audiophile, snap.Mode);
        Assert.Equal(QualityPolicy.RequireBitPerfect, snap.QualityPolicy);
        Assert.Equal("host", snap.HostPeerId);
        Assert.False(snap.Playing);
        Assert.False(snap.DspEnabled);
        Assert.False(snap.SeekingAllowed);
        Assert.True(snap.ChatCollapsed);
    }

    [Fact]
    public void PolicyFlagsSurviveRoundTrip()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Open", RoomMode.OpenLounge, "Host");
        room.QueueLocked = true;
        room.MaxMembers = 12;
        room.InviteCode = "ABC123";
        room.AutoAdvance = false;

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        Assert.True(snap.QueueLocked);
        Assert.Equal(12, snap.MaxMembers);
        Assert.Equal("ABC123", snap.InviteCode);
        Assert.False(snap.AutoAdvance);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        // Core가 새 필드를 먼저 배포해도 구형 Control이 죽지 않아야 한다.
        var snap = RoomSnapshot.Parse("""{"id":"r1","name":"X","brandNewFieldFromFuture":42}""");
        Assert.NotNull(snap);
        Assert.Equal("r1", snap!.Id);
    }

    [Fact]
    public void MalformedJsonReturnsNull()
    {
        Assert.Null(RoomSnapshot.Parse("not json"));
        Assert.Null(RoomSnapshot.Parse(""));
    }
}
