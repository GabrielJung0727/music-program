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

    [Fact]
    public void QueueAndPinsAndReactionsSurviveRoundTrip()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        rooms.Pin(room.Id, "host", 12_000, "여기 색소폰");
        rooms.React(room.Id, "host", "❤️");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        var q = Assert.Single(snap.Queue);
        Assert.Equal("tr-blue-train", q.TrackId);
        Assert.Equal("host", q.AddedByPeerId);
        Assert.False(string.IsNullOrEmpty(q.Title));

        var pin = Assert.Single(snap.Pins);
        Assert.Equal(12_000, pin.MediaTimeMs);
        Assert.Equal("여기 색소폰", pin.Text);
        Assert.Equal("host", pin.PeerId);

        Assert.Contains(snap.Reactions, r => r.Emoji == "❤️");
        Assert.Equal(4, snap.AllowedReactionEmoji.Count);
        Assert.Contains("🔥", snap.AllowedReactionEmoji);
    }

    [Fact]
    public void MembersCarryRoleAndStats()
    {
        var (rooms, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        var host = Assert.Single(snap.Members, m => m.PeerId == "host");
        Assert.Equal(MemberRole.Host, host.Role);
        Assert.False(host.Spectator);
    }

    [Fact]
    public void CollectionsAreNeverNull()
    {
        // 최소 JSON에서도 목록을 순회할 수 있어야 한다 — UI가 null 검사를 안 하도록.
        var snap = RoomSnapshot.Parse("""{"id":"r1"}""")!;
        Assert.Empty(snap.Queue);
        Assert.Empty(snap.Pins);
        Assert.Empty(snap.Members);
        Assert.Empty(snap.Outputs);
        Assert.Empty(snap.Chat);
        Assert.Empty(snap.Heatmap);
        Assert.Empty(snap.Requests);
        Assert.Empty(snap.Reactions);
        Assert.Empty(snap.AllowedReactionEmoji);
        Assert.Empty(snap.Spectators);
    }
}
