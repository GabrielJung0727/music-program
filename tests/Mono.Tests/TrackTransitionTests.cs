using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 곡이 끝까지 재생되어 다음 곡으로 넘어갈 때 Core 가 하는 일.
///
/// 출력은 다음 곡을 미리 열어 두었다가 자기 버퍼 안에서 두 곡을 잇는다. 그러려면 Core 가
/// (1) 다음 곡이 무엇인지 미리 말해 주고, (2) 실제로 그 곡으로 넘어가고, (3) 넘어간 세대가
/// "끊지 말고 이어 가도 되는 세대"임을 알려 주고, (4) 새 타임라인의 원점을 곡이 끝난 그 순간으로
/// 줘야 한다. 하나라도 어긋나면 출력은 버퍼를 비우고 새로 열어 곡 사이가 끊긴다.
/// </summary>
public class TrackTransitionTests
{
    private sealed record Stack(RoomManager Rooms, CatalogStore Catalog, string Dir);

    private static Stack NewStack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var rooms = new RoomManager(
            catalog,
            new HistoryStore(Path.Combine(dir, "h.db")),
            new StreamingHub(catalog),
            new EndpointRegistry(Path.Combine(dir, "e.db")));
        return new Stack(rooms, catalog, dir);
    }

    private static List<string> Album(Stack s, int count, long durationMs = 10_000, int sampleRate = 44100)
    {
        var artist = new Artist { Id = "ar-1", Name = "Artist" };
        var album = new Album { Id = "al-1", Title = "Album", ArtistId = artist.Id };
        var ids = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var id = $"tr-{i}";
            s.Catalog.UpsertTrack(
                new Track
                {
                    Id = id,
                    Title = $"Track {i}",
                    AlbumId = album.Id,
                    ArtistId = artist.Id,
                    TrackNumber = i,
                    DurationMs = durationMs,
                    SampleRate = sampleRate,
                    LocalPath = Path.Combine(s.Dir, $"{id}.flac")
                },
                album,
                artist);
            ids.Add(id);
        }

        return ids;
    }

    private static ListeningRoom Playing(Stack s, List<string> ids, int start = 0, RoomMode mode = RoomMode.Solo)
    {
        var room = s.Rooms.Create("me", "내 방", mode, "Listener");
        room.SmartAutoplay = false;
        var (_, error) = s.Rooms.PlayList(room.Id, "me", ids, start);
        Assert.Null(error);
        return room;
    }

    /// <summary>타임라인을 "지금 곡의 positionMs 지점"으로 옮긴다. 원점을 그만큼 과거로 민다.</summary>
    private static void At(ListeningRoom room, long positionMs)
    {
        room.MediaTimeAtOriginMs = 0;
        room.MediaOriginUnixMs = ClockSync.UnixMs() - positionMs;
    }

    private static string? Current(Stack s, ListeningRoom room) => room.CurrentTrack(s.Catalog.Tracks)?.Id;

    [Fact]
    public void NewRoomsJoinTracksGaplessly()
    {
        var s = NewStack();
        var room = s.Rooms.Create("me", "내 방", RoomMode.Solo, "Listener");

        Assert.Equal(TrackTransition.Gapless, room.Transition);
    }

    // ── 다음 곡 예고 ─────────────────────────────────────────────────────────

    [Fact]
    public void TheNextTrackIsKnownWhileTheCurrentOnePlays()
    {
        var s = NewStack();
        var ids = Album(s, 3);
        var room = Playing(s, ids);

        Assert.Equal(ids[1], s.Rooms.PeekNextTrack(room)?.Id);
    }

    [Fact]
    public void RepeatOneAnnouncesTheSameTrack()
    {
        var s = NewStack();
        var ids = Album(s, 3);
        var room = Playing(s, ids, start: 1);
        s.Rooms.SetRepeat(room.Id, "me", RepeatMode.One);

        Assert.Equal(ids[1], s.Rooms.PeekNextTrack(room)?.Id);
    }

    [Fact]
    public void TheLastTrackAnnouncesNothingUnlessTheQueueRepeats()
    {
        var s = NewStack();
        var ids = Album(s, 3);
        var room = Playing(s, ids, start: 2);

        Assert.Null(s.Rooms.PeekNextTrack(room));

        s.Rooms.SetRepeat(room.Id, "me", RepeatMode.All);
        Assert.Equal(ids[0], s.Rooms.PeekNextTrack(room)?.Id);
    }

    [Fact]
    public void NothingIsAnnouncedWhenAutoAdvanceIsOff()
    {
        var s = NewStack();
        var room = Playing(s, Album(s, 3));
        room.AutoAdvance = false;

        Assert.Null(s.Rooms.PeekNextTrack(room));
    }

    /// <summary>
    /// 셔플은 다음 곡을 곡이 끝나는 순간에 뽑았다. 이제는 미리 뽑아 두고, 실제로 넘어갈 때 그 곡을 튼다 —
    /// 예고한 곡과 트는 곡이 다르면 출력이 열어 둔 곡이 쓸모없어지고 곡 사이가 끊긴다.
    /// </summary>
    [Fact]
    public void ShuffleAnnouncesTheTrackItWillActuallyPlay()
    {
        var s = NewStack();
        var ids = Album(s, 8, durationMs: 1_000);
        var room = Playing(s, ids);
        s.Rooms.SetShuffle(room.Id, "me", true);

        for (var step = 0; step < 6; step++)
        {
            var announced = s.Rooms.PeekNextTrack(room)?.Id;
            Assert.NotNull(announced);
            // 몇 번을 물어도 같은 답이어야 한다.
            Assert.Equal(announced, s.Rooms.PeekNextTrack(room)?.Id);

            At(room, 1_050);
            s.Rooms.AdvanceFinished();

            Assert.Equal(announced, Current(s, room));
        }
    }

    [Fact]
    public void ShuffleStillVisitsEveryTrackOncePerLap()
    {
        var s = NewStack();
        var ids = Album(s, 6, durationMs: 1_000);
        var room = Playing(s, ids);
        s.Rooms.SetShuffle(room.Id, "me", true);

        var heard = new List<string?> { Current(s, room) };
        for (var step = 0; step < 5; step++)
        {
            At(room, 1_050);
            s.Rooms.AdvanceFinished();
            heard.Add(Current(s, room));
        }

        Assert.Equal(ids.OrderBy(x => x), heard.OrderBy(x => x));
    }

    // ── 넘어가는 순간 ────────────────────────────────────────────────────────

    /// <summary>
    /// 곡이 끝난 걸 100ms 늦게 알아챘다(틱 간격). 새 타임라인은 지금이 아니라 곡이 끝난 그 순간을
    /// 원점으로 삼아야 한다 — 그러면 새 곡은 이미 100ms 진행된 것으로 보인다.
    ///
    /// 읽은 값에는 그사이 흐른 벽시계 시간이 더해진다. 고정된 윗한계(예전 400ms)는 느린 CI 에서
    /// 빗나갔으므로, 실제로 흐른 시간을 재서 그만큼만 허용한다.
    /// </summary>
    [Fact]
    public void NaturalAdvanceIsSeamlessAndStartsWhereTheTrackEnded()
    {
        var s = NewStack();
        var ids = Album(s, 3, durationMs: 10_000);
        var room = Playing(s, ids);
        var before = ClockSync.UnixMs();
        At(room, 10_100);

        var changed = s.Rooms.AdvanceFinished();
        var position = room.CurrentMediaTimeMs();
        var elapsed = ClockSync.UnixMs() - before;

        Assert.Single(changed);
        Assert.Equal(ids[1], Current(s, room));
        Assert.Equal(room.ResyncEpoch, room.SeamlessEpoch);
        Assert.Equal(0, room.MediaTimeAtOriginMs);
        Assert.InRange(position, 100, 100 + elapsed);
    }

    [Fact]
    public void SkippingIsNeverSeamless()
    {
        var s = NewStack();
        var room = Playing(s, Album(s, 3));

        s.Rooms.Skip(room.Id, "me", 1);

        Assert.NotEqual(room.ResyncEpoch, room.SeamlessEpoch);
    }

    [Fact]
    public void SeekingIsNeverSeamless()
    {
        var s = NewStack();
        var room = Playing(s, Album(s, 3));
        At(room, 10_100);
        s.Rooms.AdvanceFinished();
        Assert.Equal(room.ResyncEpoch, room.SeamlessEpoch);

        s.Rooms.Seek(room.Id, "me", 3_000);

        Assert.NotEqual(room.ResyncEpoch, room.SeamlessEpoch);
    }

    [Fact]
    public void TurningTransitionsOffAdvancesTheOldWay()
    {
        var s = NewStack();
        var ids = Album(s, 3);
        var room = Playing(s, ids);
        room.Transition = TrackTransition.Off;
        At(room, 10_100);

        // 예전처럼 "지금"을 원점으로 삼는다. 그러면 읽은 값은 넘긴 뒤 흐른 시간뿐이다.
        // 곡이 끝난 순간을 원점으로 삼았다면 100ms 가 더 붙는다.
        var before = ClockSync.UnixMs();
        s.Rooms.AdvanceFinished();
        var position = room.CurrentMediaTimeMs();
        var elapsed = ClockSync.UnixMs() - before;

        Assert.Equal(ids[1], Current(s, room));
        Assert.NotEqual(room.ResyncEpoch, room.SeamlessEpoch);
        Assert.InRange(position, 0, elapsed);
    }

    /// <summary>팬아웃 룸은 Core 가 오디오를 보낸다. 출력이 직접 잇는 경로가 아니므로 예전처럼 끊고 넘긴다.</summary>
    [Fact]
    public void FanOutRoomsAdvanceTheOldWay()
    {
        var s = NewStack();
        var room = Playing(s, Album(s, 3), mode: RoomMode.OpenLounge);
        Assert.Equal(PlaybackSourceMode.FanOut, room.SourceMode);
        At(room, 10_100);

        s.Rooms.AdvanceFinished();

        Assert.NotEqual(room.ResyncEpoch, room.SeamlessEpoch);
    }

    /// <summary>PC 가 잠들었다 깨어난 것처럼 한참 늦게 알아챘으면, 지나간 만큼을 건너뛴 자리에서 잇지 않는다.</summary>
    [Fact]
    public void AVeryLateAdvanceIsNotSeamless()
    {
        var s = NewStack();
        var room = Playing(s, Album(s, 3));
        At(room, 10_000 + 30_000);

        var before = ClockSync.UnixMs();
        s.Rooms.AdvanceFinished();
        var position = room.CurrentMediaTimeMs();
        var elapsed = ClockSync.UnixMs() - before;

        Assert.NotEqual(room.ResyncEpoch, room.SeamlessEpoch);
        Assert.InRange(position, 0, elapsed);
    }

    [Fact]
    public void TheLastTrackStillStopsAtItsEnd()
    {
        var s = NewStack();
        var ids = Album(s, 2);
        var room = Playing(s, ids, start: 1);
        At(room, 10_100);

        s.Rooms.AdvanceFinished();

        Assert.False(room.Playing);
        Assert.Equal(10_000, room.MediaTimeAtOriginMs);
    }

    // ── 크로스페이드 ─────────────────────────────────────────────────────────

    /// <summary>크로스페이드는 다음 곡이 겹치는 길이만큼 먼저 시작한다. 4초 겹치면 6초 지점에서 넘어간다.</summary>
    [Fact]
    public void CrossfadeStartsTheNextTrackEarly()
    {
        var s = NewStack();
        var ids = Album(s, 3, durationMs: 10_000);
        var room = Playing(s, ids);
        room.Transition = TrackTransition.Crossfade;
        room.CrossfadeMs = 4_000;

        At(room, 5_500);
        Assert.Empty(s.Rooms.AdvanceFinished());

        var before = ClockSync.UnixMs();
        At(room, 6_100);
        s.Rooms.AdvanceFinished();
        var position = room.CurrentMediaTimeMs();
        var elapsed = ClockSync.UnixMs() - before;

        Assert.Equal(ids[1], Current(s, room));
        Assert.Equal(room.ResyncEpoch, room.SeamlessEpoch);
        Assert.InRange(position, 100, 100 + elapsed);
    }

    /// <summary>샘플레이트가 다른 두 곡은 겹칠 수 없다. 끝까지 재생한 뒤에 넘어간다.</summary>
    [Fact]
    public void CrossfadeDoesNotStartEarlyAcrossSampleRates()
    {
        var s = NewStack();
        var ids = Album(s, 2, durationMs: 10_000);
        s.Catalog.Tracks[ids[1]].SampleRate = 96_000;
        var room = Playing(s, ids);
        room.Transition = TrackTransition.Crossfade;
        room.CrossfadeMs = 4_000;

        At(room, 6_100);
        Assert.Empty(s.Rooms.AdvanceFinished());

        At(room, 10_100);
        Assert.Single(s.Rooms.AdvanceFinished());
    }

    [Theory]
    [InlineData(5_000, 200_000, 200_000, 5_000)]
    [InlineData(12_000, 10_000, 200_000, 5_000)]   // 나가는 곡의 절반까지만
    [InlineData(12_000, 200_000, 4_000, 2_000)]    // 들어오는 곡의 절반까지만
    [InlineData(60_000, 200_000, 200_000, 12_000)] // 설정의 상한
    [InlineData(5_000, 200_000, 0, 5_000)]         // 길이를 모르는 곡은 제한하지 않는다
    public void OverlapNeverExceedsHalfOfEitherTrack(int crossfadeMs, long outgoing, long incoming, long expected)
    {
        Assert.Equal(expected, RoomManager.CrossfadeOverlapMs(crossfadeMs, outgoing, incoming));
    }

    // ── 설정과 방송 ──────────────────────────────────────────────────────────

    private static CommandProcessor Commands(Stack s)
    {
        var streaming = new StreamingHub(s.Catalog, Path.Combine(s.Dir, "streaming.json"));
        return new CommandProcessor(
            s.Rooms, s.Catalog,
            new LibraryScanner(s.Catalog, new ArtworkService(Path.Combine(s.Dir, "art"))),
            streaming, new PairingService(), new HistoryStore(Path.Combine(s.Dir, "h2.db")),
            new EndpointRegistry(Path.Combine(s.Dir, "e2.db")),
            new ZoneRegistry(Path.Combine(s.Dir, "z.db")),
            new WikipediaService(new HttpClient(), Path.Combine(s.Dir, "wiki")),
            new BackupService(s.Dir),
            [Path.Combine(s.Dir, "library")]);
    }

    [Fact]
    public void TheHostChoosesTheTransition()
    {
        var s = NewStack();
        var room = s.Rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        var commands = Commands(s);

        commands.Execute("me", new MonoMessage
        {
            Type = MessageTypes.SetTransition, RoomId = room.Id, Index = (int)TrackTransition.Crossfade, MediaTimeMs = 8_000
        }, "Listener");

        Assert.Equal(TrackTransition.Crossfade, room.Transition);
        Assert.Equal(8_000, room.CrossfadeMs);
    }

    [Theory]
    [InlineData(0, 1_000)]
    [InlineData(500, 1_000)]
    [InlineData(99_000, 12_000)]
    public void CrossfadeLengthIsKeptBetweenOneAndTwelveSeconds(long asked, int kept)
    {
        var s = NewStack();
        var room = s.Rooms.Create("me", "내 방", RoomMode.Solo, "Listener");

        Commands(s).Execute("me", new MonoMessage
        {
            Type = MessageTypes.SetTransition, RoomId = room.Id, MediaTimeMs = asked
        }, "Listener");

        Assert.Equal(kept, room.CrossfadeMs);
        Assert.Equal(TrackTransition.Gapless, room.Transition);   // 방식은 보내지 않았으므로 그대로다
    }

    [Fact]
    public void AnUnknownTransitionValueIsIgnored()
    {
        var s = NewStack();
        var room = s.Rooms.Create("me", "내 방", RoomMode.Solo, "Listener");

        Commands(s).Execute("me", new MonoMessage { Type = MessageTypes.SetTransition, RoomId = room.Id, Index = 9 }, "Listener");

        Assert.Equal(TrackTransition.Gapless, room.Transition);
    }

    [Fact]
    public void TheSnapshotCarriesTheTransition()
    {
        var s = NewStack();
        var room = s.Rooms.Create("me", "내 방", RoomMode.Solo, "Listener");
        room.Transition = TrackTransition.Crossfade;
        room.CrossfadeMs = 7_000;

        var snap = RoomSnapshot.Parse(s.Rooms.SnapshotJson(room))!;

        Assert.Equal(TrackTransition.Crossfade, snap.Transition);
        Assert.Equal(7_000, snap.CrossfadeMs);
    }

    /// <summary>출력이 받는 room_state 에 다음 곡의 경로와 이어 가도 되는 세대가 실려 있다.</summary>
    [Fact]
    public void TheBroadcastTellsOutputsWhatComesNext()
    {
        var s = NewStack();
        var ids = Album(s, 3);
        var room = Playing(s, ids);
        var broadcaster = new RoomBroadcaster(
            new ConnectionRegistry(), s.Rooms, s.Catalog, new StreamingHub(s.Catalog, Path.Combine(s.Dir, "st.json")));

        var before = broadcaster.StateMessage(room);
        Assert.Equal(ids[0], before.TrackId);
        Assert.Equal(ids[1], before.NextTrackId);
        Assert.Equal(s.Catalog.Tracks[ids[1]].LocalPath, before.NextLocalPath);
        Assert.Equal(TrackTransition.Gapless, before.Transition);
        Assert.NotEqual(before.Epoch, before.SeamlessEpoch);

        At(room, 10_100);
        s.Rooms.AdvanceFinished();

        var after = broadcaster.StateMessage(room);
        Assert.Equal(ids[1], after.TrackId);
        Assert.Equal(ids[2], after.NextTrackId);
        Assert.Equal(after.Epoch, after.SeamlessEpoch);
    }

    /// <summary>팬아웃 룸의 출력은 파일을 직접 열지 않는다. 경로를 실어 보낼 이유가 없다.</summary>
    [Fact]
    public void FanOutBroadcastsCarryNoPaths()
    {
        var s = NewStack();
        var room = Playing(s, Album(s, 3), mode: RoomMode.OpenLounge);
        var broadcaster = new RoomBroadcaster(
            new ConnectionRegistry(), s.Rooms, s.Catalog, new StreamingHub(s.Catalog, Path.Combine(s.Dir, "st.json")));

        var msg = broadcaster.StateMessage(room);

        Assert.Null(msg.LocalPath);
        Assert.Null(msg.NextTrackId);
        Assert.Null(msg.NextLocalPath);
    }
}
