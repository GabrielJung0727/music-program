using System.Text.Json;
using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 0.5.6 QA 에서 올라온 재생 순서·탐색 문제들.
///
/// - 앨범 가운데 곡을 누르면 그 한 곡만 큐 끝에 붙어 순서가 뒤엉킨 것처럼 보였다.
/// - 셔플·반복은 버튼만 있고 상태가 없어서 켜졌는지 알 수 없었다.
/// - 길이를 모르는 트랙에서 추정치(3분) 뒤로 탐색하면 곡이 끝난 것으로 처리됐다.
/// </summary>
public class TransportOrderTests
{
    private static (RoomManager Rooms, CatalogStore Catalog) NewStack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var rooms = new RoomManager(
            catalog,
            new HistoryStore(Path.Combine(dir, "h.db")),
            new StreamingHub(catalog),
            new EndpointRegistry(Path.Combine(dir, "e.db")));
        return (rooms, catalog);
    }

    /// <summary>같은 룸·카탈로그 위에 명령 처리기를 얹는다 — 와이어에서 오는 메시지를 그대로 흘려보내려고.</summary>
    private static CommandProcessor NewCommands(RoomManager rooms, CatalogStore catalog)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var streaming = new StreamingHub(catalog, Path.Combine(dir, "streaming.json"));
        var history = new HistoryStore(Path.Combine(dir, "h.db"));
        var endpoints = new EndpointRegistry(Path.Combine(dir, "e.db"));
        return new CommandProcessor(
            rooms, catalog,
            new LibraryScanner(catalog, new ArtworkService(Path.Combine(dir, "art"))),
            streaming, new PairingService(), history, endpoints,
            new ZoneRegistry(Path.Combine(dir, "z.db")),
            new WikipediaService(new HttpClient(), Path.Combine(dir, "wiki")),
            new BackupService(dir),
            [Path.Combine(dir, "library")]);
    }

    private static List<string> Album(CatalogStore catalog, int count, long durationMs = 240_000)
    {
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
                    DurationMs = durationMs
                },
                album,
                artist);
            ids.Add(id);
        }

        return ids;
    }

    /// <summary>앨범 가운데 곡을 골라도 앨범 전체가 큐가 되고, 고른 곡이 시작점이 된다.</summary>
    [Fact]
    public void PlayingFromTheMiddleQueuesTheWholeAlbum()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 5);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");

        var (updated, error) = rooms.PlayList(room.Id, "me", ids, 2);

        Assert.Null(error);
        Assert.NotNull(updated);
        Assert.Equal(ids, updated!.Queue.Select(q => q.TrackId).ToList());
        Assert.Equal(2, updated.QueueIndex);
        Assert.True(updated.Playing);
        Assert.Equal(0, updated.MediaTimeAtOriginMs);
    }

    /// <summary>같은 목록을 다시 걸어도 큐가 두 배로 늘지 않는다.</summary>
    [Fact]
    public void PlayingAnAlbumTwiceDoesNotStackTheQueue()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 3);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");

        rooms.PlayList(room.Id, "me", ids, 0);
        var (updated, _) = rooms.PlayList(room.Id, "me", ids, 1);

        Assert.Equal(3, updated!.Queue.Count);
        Assert.Equal(1, updated.QueueIndex);
    }

    /// <summary>한 곡 반복은 큐를 건드리지 않고 같은 칸을 처음부터 다시 연다.</summary>
    [Fact]
    public void RepeatOneRestartsTheSameTrack()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 3, durationMs: 1_000);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 0);
        rooms.SetRepeat(room.Id, "me", RepeatMode.One);

        // 곡이 끝난 시점으로 타임라인을 밀어 둔다.
        room.MediaTimeAtOriginMs = 2_000;

        rooms.AdvanceFinished();

        Assert.Equal(0, room.QueueIndex);
        Assert.Equal(0, room.MediaTimeAtOriginMs);
    }

    /// <summary>전체 반복은 마지막 곡 다음에 처음으로 돌아간다.</summary>
    [Fact]
    public void RepeatAllWrapsToTheTop()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 3, durationMs: 1_000);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 2);
        rooms.SetRepeat(room.Id, "me", RepeatMode.All);
        room.MediaTimeAtOriginMs = 2_000;

        rooms.AdvanceFinished();

        Assert.Equal(0, room.QueueIndex);
        Assert.True(room.Playing);
    }

    /// <summary>셔플은 큐의 순서를 바꾸지 않는다 — 끄면 앨범 순서가 그대로 돌아와야 한다.</summary>
    [Fact]
    public void ShuffleLeavesTheQueueOrderAlone()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 6, durationMs: 1_000);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 0);
        rooms.SetShuffle(room.Id, "me", true);

        rooms.Skip(room.Id, "me", 1);

        Assert.Equal(ids, room.Queue.Select(q => q.TrackId).ToList());
        Assert.True(room.Shuffle);
    }

    /// <summary>셔플 + 전체 반복은 방금 튼 곡을 바로 또 고르지 않는다.</summary>
    [Fact]
    public void ShuffleMovesOffTheCurrentTrack()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 6, durationMs: 1_000);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 0);
        rooms.SetShuffle(room.Id, "me", true);
        rooms.SetRepeat(room.Id, "me", RepeatMode.All);

        for (var i = 0; i < 20; i++)
        {
            var before = room.QueueIndex;
            rooms.Skip(room.Id, "me", 1);
            Assert.NotEqual(before, room.QueueIndex);
        }
    }

    /// <summary>
    /// 길이를 모르는 트랙에서 3분 뒤로 탐색해도 곡이 끝나지 않는다.
    ///
    /// 예전에는 추정치 3분을 트랙 길이로 써서, 그 뒤로 탐색하면 그 자리에서 "다 들었다"가
    /// 되고 오토플레이가 같은 곡을 다시 큐에 넣었다 — 탐색했더니 곡이 처음부터 다시
    /// 시작하는 것으로 보인 원인이다.
    /// </summary>
    [Fact]
    public void SeekingPastTheDurationGuessDoesNotEndTheTrack()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 1, durationMs: 0);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 0);

        var (_, error) = rooms.Seek(room.Id, "me", 200_000);

        Assert.Null(error);
        Assert.Equal(200_000, room.MediaTimeAtOriginMs);

        rooms.AdvanceFinished();

        Assert.True(room.Playing);
        Assert.Single(room.Queue);
    }

    /// <summary>아는 길이보다 뒤로는 탐색하지 않는다.</summary>
    [Fact]
    public void SeekingIsClampedToAKnownDuration()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 1, durationMs: 120_000);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 0);

        rooms.Seek(room.Id, "me", 999_000);

        Assert.Equal(120_000, room.MediaTimeAtOriginMs);
    }

    /// <summary>탐색은 세대를 올린다 — Output 은 그걸 보고 버퍼를 비우고 다시 맞춘다.</summary>
    [Fact]
    public void SeekingBumpsTheResyncEpoch()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 1);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 0);
        var before = room.ResyncEpoch;

        rooms.Seek(room.Id, "me", 60_000);

        Assert.True(room.ResyncEpoch > before);
        Assert.Equal(60_000, room.MediaTimeAtOriginMs);
    }

    /// <summary>
    /// 길이를 모르면 추정치를 화면에 내보내지 않는다.
    ///
    /// 실제보다 짧은 길이를 진행 바에 물리면 그 지점에서 바가 끝에 붙어 멈춘다 —
    /// 노래는 계속 나오는데 바만 안 움직이는 증상이다. 0 을 주고 화면이 눈금 없이
    /// 그리게 한다.
    /// </summary>
    [Fact]
    public void AnUnknownDurationIsReportedAsUnknown()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 1, durationMs: 0);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 0);

        Assert.Equal(0, rooms.EffectiveDurationOf(room));

        using var snapshot = JsonDocument.Parse(rooms.SnapshotJson(room));
        Assert.Equal(0, snapshot.RootElement.GetProperty("durationMs").GetInt64());
    }

    /// <summary>아는 길이는 그대로 나간다.</summary>
    [Fact]
    public void AKnownDurationIsReportedAsIs()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 1, durationMs: 321_000);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 0);

        Assert.Equal(321_000, rooms.EffectiveDurationOf(room));

        using var snapshot = JsonDocument.Parse(rooms.SnapshotJson(room));
        Assert.Equal(321_000, snapshot.RootElement.GetProperty("durationMs").GetInt64());
    }

    /// <summary>재생하며 알아낸 길이는 카탈로그에 남는다 — 다음부터는 처음부터 제대로 보인다.</summary>
    [Fact]
    public void ADiscoveredDurationIsWrittenBack()
    {
        var (_, catalog) = NewStack();
        var ids = Album(catalog, 1, durationMs: 0);

        catalog.SetTrackDuration(ids[0], 275_000);

        Assert.Equal(275_000, catalog.Tracks[ids[0]].DurationMs);
    }

    /// <summary>
    /// 웹은 skip 을 delta 로 보낸다. 예전에는 Core 가 index 만 읽어서 delta 가 통째로
    /// 무시됐고, 기본값 1 이 쓰여 「이전」을 눌러도 다음 곡으로 넘어갔다.
    /// </summary>
    [Fact]
    public void PreviousFromTheWebGoesBackNotForward()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 3);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        var commands = NewCommands(rooms, catalog);
        rooms.PlayList(room.Id, "me", ids, 1);

        commands.Execute("me", new MonoMessage { Type = MessageTypes.Skip, RoomId = room.Id, Delta = -1 }, "Listener");

        Assert.Equal(0, room.QueueIndex);
    }

    /// <summary>CLI 는 index 로 보낸다. 그쪽도 그대로 동작해야 한다.</summary>
    [Fact]
    public void PreviousFromTheCliStillWorks()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 3);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        var commands = NewCommands(rooms, catalog);
        rooms.PlayList(room.Id, "me", ids, 2);

        commands.Execute("me", new MonoMessage { Type = MessageTypes.Skip, RoomId = room.Id, Index = -1 }, "Listener");

        Assert.Equal(1, room.QueueIndex);
    }

    /// <summary>
    /// 한참 듣다가 누른 「이전」은 이 곡을 처음부터 다시 튼다 — 실수로 한 번 눌렀다고
    /// 듣던 곡을 잃지 않는다.
    /// </summary>
    [Fact]
    public void PreviousRestartsTheTrackWhenYouAreIntoIt()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 3);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 1);
        rooms.Seek(room.Id, "me", 30_000);

        rooms.Skip(room.Id, "me", -1);

        Assert.Equal(1, room.QueueIndex);
        Assert.Equal(0, room.MediaTimeAtOriginMs);
    }

    /// <summary>곡의 맨 앞에서 누르면 그때는 앞 곡으로 간다.</summary>
    [Fact]
    public void PreviousGoesBackWhenYouAreAtTheStart()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 3);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 1);

        rooms.Skip(room.Id, "me", -1);

        Assert.Equal(0, room.QueueIndex);
    }

    /// <summary>「다음」은 그대로 다음 곡이다.</summary>
    [Fact]
    public void NextStillGoesForward()
    {
        var (rooms, catalog) = NewStack();
        var ids = Album(catalog, 3);
        var room = rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        rooms.PlayList(room.Id, "me", ids, 0);
        rooms.Seek(room.Id, "me", 30_000);

        rooms.Skip(room.Id, "me", 1);

        Assert.Equal(1, room.QueueIndex);
    }
}
