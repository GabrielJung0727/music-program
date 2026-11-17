using Mono.Core;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 대기열에서 곡을 빼다 보면 다른 곡이 저절로 재생되던 문제.
///
/// 화면은 "몇 번째 칸"을 보냈다. X 를 눌러 한 곡을 빼면 아래 곡이 그 자리로 올라오고,
/// 이어서 한 번 더 누르면 브라우저가 두 번을 더블클릭으로 묶어 "이 칸으로 이동"까지 보냈다.
/// 그 칸에는 이미 다음 곡이 올라와 있어서 그 곡이 재생됐다. 화면은 버튼에서 올라온
/// 더블클릭을 걸러 내고, Core 는 칸 번호와 함께 온 항목 id 로 곡을 찾는다.
/// </summary>
public class QueueEditTests
{
    private static (RoomManager Rooms, ListeningRoom Room, List<string> Ids) Playing(int count, int startIndex)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var rooms = new RoomManager(
            catalog,
            new HistoryStore(Path.Combine(dir, "h.db")),
            new StreamingHub(catalog),
            new EndpointRegistry(Path.Combine(dir, "e.db")));

        var artist = new Artist { Id = "ar-1", Name = "Artist" };
        var album = new Album { Id = "al-1", Title = "Album", ArtistId = artist.Id };
        var ids = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var id = $"tr-{i}";
            catalog.UpsertTrack(
                new Track
                {
                    Id = id,
                    Title = $"Track {i}",
                    AlbumId = album.Id,
                    ArtistId = artist.Id,
                    TrackNumber = i,
                    DurationMs = 240_000
                },
                album,
                artist);
            ids.Add(id);
        }

        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        var (_, error) = rooms.PlayList(room.Id, "me", ids, startIndex);
        Assert.Null(error);
        return (rooms, room, ids);
    }

    /// <summary>
    /// X 를 연달아 누른 것이 더블클릭으로 묶여 따라온 「이동」은, 방금 뺀 항목을 가리킨다.
    /// 그 자리로 올라온 다음 곡을 틀면 안 된다.
    /// </summary>
    [Fact]
    public void JumpAimedAtARemovedItemDoesNotPlayWhateverSlidIntoItsSlot()
    {
        var (rooms, room, ids) = Playing(5, 0);
        var epoch = room.ResyncEpoch;
        var removed = room.Queue[2].Id;

        rooms.RemoveQueue(room.Id, "me", 2, removed);
        var (updated, error) = rooms.JumpTo(room.Id, "me", 2, removed);

        Assert.Null(error);
        Assert.NotNull(updated);
        Assert.Equal(0, room.QueueIndex);
        Assert.Equal(ids[0], room.Queue[room.QueueIndex].TrackId);
        Assert.Equal(epoch, room.ResyncEpoch);
    }

    /// <summary>칸 번호가 밀렸어도 id 가 가리키는 그 곡만 빠진다.</summary>
    [Fact]
    public void RemoveFollowsTheItemNotTheStaleIndex()
    {
        var (rooms, room, ids) = Playing(5, 0);
        var target = room.Queue[3].Id;

        // 화면이 4번째 칸을 누르는 사이에 누군가 그 앞의 곡을 먼저 뺐다.
        rooms.RemoveQueue(room.Id, "me", 1, room.Queue[1].Id);
        rooms.RemoveQueue(room.Id, "me", 3, target);

        Assert.Equal(new[] { ids[0], ids[2], ids[4] }, room.Queue.Select(q => q.TrackId));
    }

    /// <summary>같은 항목을 두 번 빼라고 해도 두 번째는 아무 곡도 건드리지 않는다.</summary>
    [Fact]
    public void RemovingAnItemTwiceRemovesItOnce()
    {
        var (rooms, room, ids) = Playing(4, 0);
        var target = room.Queue[1].Id;

        rooms.RemoveQueue(room.Id, "me", 1, target);
        var (_, error) = rooms.RemoveQueue(room.Id, "me", 1, target);

        Assert.Null(error);
        Assert.Equal(new[] { ids[0], ids[2], ids[3] }, room.Queue.Select(q => q.TrackId));
    }

    /// <summary>재생 중인 곡 앞의 곡을 빼도 지금 곡과 타임라인은 그대로다.</summary>
    [Fact]
    public void RemovingAnEarlierTrackLeavesPlaybackAlone()
    {
        var (rooms, room, ids) = Playing(5, 2);
        var epoch = room.ResyncEpoch;

        rooms.RemoveQueue(room.Id, "me", 0, room.Queue[0].Id);

        Assert.Equal(1, room.QueueIndex);
        Assert.Equal(ids[2], room.Queue[room.QueueIndex].TrackId);
        Assert.True(room.Playing);
        Assert.Equal(epoch, room.ResyncEpoch);
    }

    /// <summary>
    /// 재생 중인 곡을 빼면 그 자리로 올라온 곡이 처음부터 시작한다. 세대가 올라가야
    /// 출력이 뺀 곡의 소리를 버린다.
    /// </summary>
    [Fact]
    public void RemovingThePlayingTrackStartsTheNextOneFromTheTop()
    {
        var (rooms, room, ids) = Playing(3, 1);
        var epoch = room.ResyncEpoch;
        room.MediaTimeAtOriginMs = 90_000;

        rooms.RemoveQueue(room.Id, "me", 1, room.Queue[1].Id);

        Assert.Equal(1, room.QueueIndex);
        Assert.Equal(ids[2], room.Queue[room.QueueIndex].TrackId);
        Assert.True(room.Playing);
        Assert.Equal(0, room.MediaTimeAtOriginMs);
        Assert.True(room.ResyncEpoch > epoch);
    }

    /// <summary>재생 중인 마지막 곡을 빼면 앞 곡을 대신 틀지 않고 멈춘다.</summary>
    [Fact]
    public void RemovingThePlayingLastTrackStopsInsteadOfPlayingThePreviousOne()
    {
        var (rooms, room, _) = Playing(3, 2);
        var epoch = room.ResyncEpoch;

        rooms.RemoveQueue(room.Id, "me", 2, room.Queue[2].Id);

        Assert.Equal(2, room.Queue.Count);
        Assert.False(room.Playing);
        Assert.Equal(0, room.MediaTimeAtOriginMs);
        Assert.True(room.ResyncEpoch > epoch);
    }

    /// <summary>id 없이 칸 번호만 보내는 예전 클라이언트(Mono.Cli)는 그대로 동작한다.</summary>
    [Fact]
    public void IndexOnlyCommandsStillWork()
    {
        var (rooms, room, ids) = Playing(4, 0);

        rooms.RemoveQueue(room.Id, "me", 3);
        rooms.JumpTo(room.Id, "me", 2);

        Assert.Equal(3, room.Queue.Count);
        Assert.Equal(ids[2], room.Queue[room.QueueIndex].TrackId);
    }
}
