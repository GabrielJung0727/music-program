using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 라운지 관리자 패널이 읽는 값이 스냅샷에 실리는지 고정한다.
/// Core 가 이름이나 형태를 바꾸면 패널이 조용히 비활성으로 굳는다.
/// </summary>
public class LoungeSnapshotTests
{
    private static RoomManager NewRooms()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var history = new HistoryStore(Path.Combine(dir, "h.db"));
        var streaming = new StreamingHub(catalog);
        var endpoints = new EndpointRegistry(Path.Combine(dir, "e.db"));
        return new RoomManager(catalog, history, streaming, endpoints);
    }

    [Fact]
    public void HostIsIdentifiableFromTheSnapshot()
    {
        var rooms = NewRooms();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        Assert.Equal("host", snap.HostPeerId);
        Assert.Contains(snap.Members, m => m.PeerId == "host" && m.Role == MemberRole.Host);
    }

    [Fact]
    public void InviteRotationSurfacesCodeAndExpiry()
    {
        var rooms = NewRooms();
        var room = rooms.Create("host", "Lounge", RoomMode.Invite, "Host");
        rooms.ManageInvite(room.Id, "host", InviteAction.Rotate, 30);

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        Assert.False(string.IsNullOrEmpty(snap.InviteCode));
        Assert.NotNull(snap.InviteExpiresAt);
    }

    [Fact]
    public void GuestRequestsAppearForTheHost()
    {
        var rooms = NewRooms();
        var room = rooms.Create("host", "DJ", RoomMode.HostQueue, "Host");
        rooms.Request(room.Id, "guest", "tr-blue-train");

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        var req = Assert.Single(snap.Requests);
        Assert.Equal("tr-blue-train", req.TrackId);
        Assert.Equal("guest", req.FromPeerId);
        Assert.False(string.IsNullOrEmpty(req.Title));
    }

    [Fact]
    public void PolicyFlagsRoundTripForThePanel()
    {
        var rooms = NewRooms();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");
        rooms.ApplyHostSettings(room.Id, "host", r =>
        {
            r.QueueLocked = true;
            r.SeekingAllowed = false;
            r.MaxMembers = 8;
            r.QualityPolicy = QualityPolicy.LowestCommonFormat;
            r.SourceMode = PlaybackSourceMode.FanOut;
        });

        var snap = RoomSnapshot.Parse(rooms.SnapshotJson(room))!;

        Assert.True(snap.QueueLocked);
        Assert.False(snap.SeekingAllowed);
        Assert.Equal(8, snap.MaxMembers);
        Assert.Equal(QualityPolicy.LowestCommonFormat, snap.QualityPolicy);
        Assert.Equal(PlaybackSourceMode.FanOut, snap.SourceMode);
    }

    [Fact]
    public void NonHostSettingsAreRefused()
    {
        // Control 이 비활성으로 막기 전에 Core 가 최종 방어선이다.
        var rooms = NewRooms();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "Host");

        var result = rooms.ApplyHostSettings(room.Id, "guest", r => r.QueueLocked = true);

        Assert.Equal("only host", result.Error);
        Assert.False(rooms.Get(room.Id)!.QueueLocked);
    }
}
