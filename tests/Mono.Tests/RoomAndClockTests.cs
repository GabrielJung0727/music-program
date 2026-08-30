using Mono.Core;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

public class RoomAndClockTests
{
    private static (RoomManager Rooms, CatalogStore Catalog, HistoryStore History, StreamingHub Streaming) NewStack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var history = new HistoryStore(Path.Combine(dir, "h.db"));
        var streaming = new StreamingHub(catalog);
        var endpoints = new EndpointRegistry(Path.Combine(dir, "e.db"));
        return (new RoomManager(catalog, history, streaming, endpoints), catalog, history, streaming);
    }

    [Fact]
    public void AudiophileModeLocksDspAndSeek()
    {
        var room = ListeningRoom.ForMode("r1", "Quiet", "host", RoomMode.Audiophile);
        Assert.False(room.DspEnabled);
        Assert.False(room.SeekingAllowed);
        Assert.True(room.ChatCollapsed);
        Assert.Equal(QualityPolicy.RequireBitPerfect, room.QualityPolicy);
    }

    [Fact]
    public void HostQueueRejectsGuestEnqueue()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "DJ", RoomMode.HostQueue, "Host");
        var result = rooms.Enqueue(room.Id, "guest", "tr-blue-train");
        Assert.Equal("only the host may queue; use request_track", result.Error);
        var req = rooms.Request(room.Id, "guest", "tr-blue-train");
        Assert.Null(req.Error);
        var ok = rooms.Enqueue(room.Id, "host", "tr-blue-train");
        Assert.Null(ok.Error);
        Assert.Single(ok.Room!.Queue);
    }

    [Fact]
    public void DelegatedDjMayDirectPlayback()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "DJ", RoomMode.HostQueue, "Host");
        rooms.Join(room.Id, "second", PeerRole.Control, null, "Second");
        Assert.Equal("only the host may queue; use request_track", rooms.Enqueue(room.Id, "second", "tr-so-what").Error);

        rooms.SetRole(room.Id, "host", "second", MemberRole.Dj);
        Assert.Null(rooms.Enqueue(room.Id, "second", "tr-so-what").Error);
        Assert.Null(rooms.Play(room.Id, "second").Error);
    }

    [Fact]
    public void ArchiveKeepsPinnedTracksAsHighlights()
    {
        var (rooms, _, history, _) = NewStack();
        var room = rooms.Create("host", "Night", RoomMode.OpenLounge, "H");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        rooms.Enqueue(room.Id, "host", "tr-so-what");
        rooms.Play(room.Id, "host");
        rooms.Pin(room.Id, "host", 12000, "horn entrance");
        var ended = rooms.EndAndMaybeArchive(room.Id, "host", consent: true);

        Assert.NotNull(ended.Archive);
        Assert.Contains("tr-blue-train", ended.Archive!.HighlightTrackIds);
        Assert.DoesNotContain("tr-so-what", ended.Archive.HighlightTrackIds);
        Assert.NotNull(ended.Playlist);
        // 아카이브와 플레이리스트는 개인 라이브러리에 남는다.
        Assert.Contains(history.Archives, a => a.Id == ended.Archive.Id);
        Assert.Contains(history.Playlists, p => p.Id == ended.Playlist!.Id);
    }

    [Fact]
    public void DiscardedSessionLeavesNothingBehind()
    {
        var (rooms, _, history, _) = NewStack();
        var room = rooms.Create("host", "Ephemeral", RoomMode.OpenLounge, "H");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        rooms.Play(room.Id, "host");
        var ended = rooms.EndAndMaybeArchive(room.Id, "host", consent: false);

        Assert.Null(ended.Archive);
        Assert.Empty(history.Archives);
        Assert.Null(rooms.Get(room.Id));
    }

    [Fact]
    public void AnonymizedArchiveDropsAuthorsAndChat()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "Private", RoomMode.OpenLounge, "H");
        rooms.ApplyHostSettings(room.Id, "host", r => r.AnonymizeArchive = true);
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        rooms.Play(room.Id, "host");
        rooms.Pin(room.Id, "host", 1000, "here");
        rooms.Chat(room.Id, "host", "hello");
        var ended = rooms.EndAndMaybeArchive(room.Id, "host", consent: true);

        Assert.NotNull(ended.Archive);
        Assert.All(ended.Archive!.Pins, p => Assert.Equal("anon", p.PeerId));
        Assert.Empty(ended.Archive.Chat);
        Assert.Empty(ended.Archive.Participants);
    }

    [Fact]
    public void SessionAdvancesToNextTrackWhenFinished()
    {
        var (rooms, catalog, _, _) = NewStack();
        var room = rooms.Create("host", "Auto", RoomMode.OpenLounge, "H");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        rooms.Enqueue(room.Id, "host", "tr-so-what");
        rooms.Play(room.Id, "host");

        // 첫 곡이 끝난 시점으로 타임라인을 밀어 놓는다.
        var live = rooms.Get(room.Id)!;
        live.MediaTimeAtOriginMs = catalog.Tracks["tr-blue-train"].DurationMs + 10;
        var changed = rooms.AdvanceFinished();

        Assert.Single(changed);
        Assert.Equal(1, live.QueueIndex);
        Assert.True(live.Playing);
        Assert.Equal("tr-so-what", live.Queue[live.QueueIndex].TrackId);
    }

    [Fact]
    public void LastTrackStopsInsteadOfLooping()
    {
        var (rooms, catalog, _, _) = NewStack();
        var room = rooms.Create("host", "Auto", RoomMode.OpenLounge, "H");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        rooms.Play(room.Id, "host");

        var live = rooms.Get(room.Id)!;
        live.MediaTimeAtOriginMs = catalog.Tracks["tr-blue-train"].DurationMs + 10;
        rooms.AdvanceFinished();

        Assert.False(live.Playing);
        Assert.Equal(0, live.QueueIndex);
    }

    [Fact]
    public void PinClickSeeksWithinTheSameTrack()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "H");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        rooms.Play(room.Id, "host");
        rooms.Pin(room.Id, "host", 12000, "horn");
        var pinId = rooms.Get(room.Id)!.Pins[0].Id;

        Assert.Null(rooms.SeekToPin(room.Id, "host", pinId).Error);
        Assert.Equal(12000, rooms.Get(room.Id)!.MediaTimeAtOriginMs);
    }

    [Fact]
    public void AudiophileRoomRefusesSeeking()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "Quiet", RoomMode.Audiophile, "H");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        var seek = rooms.Seek(room.Id, "host", 5000);
        Assert.Equal("audiophile mode: skip tracks instead of seeking", seek.Error);
    }

    [Fact]
    public void DigitalVolumeIsRefusedOnBitPerfectRooms()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "Quiet", RoomMode.Audiophile, "H");
        rooms.RegisterOutput(room.Id, new OutputCapability
        {
            PeerId = "dac",
            DisplayName = "USB DAC",
            HardwareVolume = false
        });

        var refused = rooms.SetVolume(room.Id, "host", "dac", 60);
        Assert.NotNull(refused.Error);
        Assert.Equal(100, rooms.Get(room.Id)!.Outputs["dac"].VolumePercent);

        // 하드웨어 볼륨이 있는 기기는 그대로 통과한다.
        rooms.RegisterOutput(room.Id, new OutputCapability { PeerId = "amp", DisplayName = "Amp", HardwareVolume = true });
        Assert.Null(rooms.SetVolume(room.Id, "host", "amp", 60).Error);
        Assert.Equal(60, rooms.Get(room.Id)!.Outputs["amp"].VolumePercent);
    }

    [Fact]
    public void StreamingTrackNeedsALinkedAccount()
    {
        var (rooms, _, _, streaming) = NewStack();
        streaming.Link(StreamingProvider.Tidal, "token", "Tidal");
        var streamingTrack = "tr-tidal-time-out";
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "H");
        Assert.Null(rooms.Enqueue(room.Id, "host", streamingTrack).Error);

        streaming.Unlink(StreamingProvider.Tidal);
        var second = rooms.Create("host", "Lounge2", RoomMode.OpenLounge, "H");
        var blocked = rooms.Enqueue(second.Id, "host", streamingTrack);
        Assert.Contains("계정이 연결되지", blocked.Error);
    }

    [Fact]
    public void StreamingTrackFallsBackToClockSyncEvenInFanOutRoom()
    {
        var (_, catalog, _, streaming) = NewStack();
        streaming.Link(StreamingProvider.Qobuz, "token", "Qobuz");
        var track = catalog.Tracks["tr-qobuz-spectrum"];

        using var source = PlaybackSourceFactory.For(track, PlaybackSourceMode.FanOut);
        Assert.Equal(PlaybackSourceMode.ClockSync, source.Mode);
        Assert.False(source.ProducesDataPlane);
        Assert.Empty(source.Read(0, 20));
    }

    [Fact]
    public void QueueMoveKeepsPlayingTrack()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "H");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");
        rooms.Enqueue(room.Id, "host", "tr-moment-notice");
        rooms.Enqueue(room.Id, "host", "tr-so-what");
        rooms.Play(room.Id, "host");

        rooms.MoveQueue(room.Id, "host", 0, 2);
        var live = rooms.Get(room.Id)!;
        Assert.Equal("tr-blue-train", live.Queue[live.QueueIndex].TrackId);
        Assert.Equal(2, live.QueueIndex);
    }

    [Fact]
    public void KickedPeerCannotRejoin()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "H");
        rooms.Join(room.Id, "guest", PeerRole.Control, null, "G");
        rooms.Kick(room.Id, "host", "guest");
        Assert.Equal("kicked from this room", rooms.Join(room.Id, "guest", PeerRole.Control, null, "G").Error);
    }

    [Fact]
    public void InviteCanBeRotatedAndRevoked()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "Private", RoomMode.Invite, "H");
        var first = room.InviteCode;

        rooms.ManageInvite(room.Id, "host", InviteAction.Rotate, 30);
        Assert.NotEqual(first, rooms.Get(room.Id)!.InviteCode);
        Assert.Equal("invite code required", rooms.Join(room.Id, "guest", PeerRole.Control, first, "G").Error);

        rooms.ManageInvite(room.Id, "host", InviteAction.Revoke, 0);
        Assert.Equal("invite revoked", rooms.Join(room.Id, "guest", PeerRole.Control, "123456", "G").Error);
    }

    [Fact]
    public void ClockOffsetAveragesSymmetricDelay()
    {
        Assert.Equal(0, ClockSync.OffsetMs(0, 10, 10, 20));
        Assert.Equal(20, ClockSync.RttMs(0, 10, 10, 20));
    }

    [Fact]
    public void JitterBufferGrowsWithRttAndStaysBounded()
    {
        // LAN: 5ms 목표에 가깝게
        Assert.InRange(ClockSync.JitterBufferMs(1, 0.2), 5, 8);
        // WAN: 락 유지를 위해 커지되 80ms를 넘지 않는다.
        Assert.Equal(80, ClockSync.JitterBufferMs(400, 30));
    }

    [Fact]
    public void LyricsFollowMediaTime()
    {
        var lines = LyricsParser.Parse("[00:00.00]a\n[00:12.00]horn\n[00:48.00]head");
        Assert.Equal("horn", LyricsParser.At(lines, 12_000)!.Text);
        Assert.Equal("a", LyricsParser.At(lines, 100)!.Text);
    }

    [Fact]
    public void AudiophileDspIsBitPerfect()
    {
        var room = ListeningRoom.ForMode("r", "a", "h", RoomMode.Audiophile);
        room.DspEnabled = true;
        room.DspPreset = DspPresetKind.Headphones;
        var pcm = new byte[] { 1, 2, 3, 4 };
        var outPcm = DspPipeline.Process(pcm, 44100, 16, 2, room, out _, out _, out var src);
        Assert.False(src);
        Assert.Equal(pcm, outPcm);
    }

    [Fact]
    public void InviteJoinRequiresCode()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "Private", RoomMode.Invite, "H");
        var bad = rooms.Join(room.Id, "g", PeerRole.Control, "000000", "G");
        Assert.Equal("invite code required", bad.Error);
        var ok = rooms.Join(room.Id, "g", PeerRole.Control, room.InviteCode, "G");
        Assert.Null(ok.Error);
    }

    [Fact]
    public void QualityPolicySpectatesIncompatibleDsd()
    {
        var track = new Track { Id = "t", Title = "d", AlbumId = "a", ArtistId = "r", IsDsd = true, DsdRate = 64, SampleRate = 2822400, BitDepth = 1 };
        var cap = new OutputCapability { PeerId = "o", DisplayName = "dac", SupportsDsd = false };
        var room = ListeningRoom.ForMode("r", "n", "h", RoomMode.Audiophile);
        var d = QualityPolicyEngine.Evaluate(room, track, cap);
        Assert.True(d.Spectator);
    }

    [Fact]
    public void LowestCommonFormatMarksSrcInsteadOfSpectating()
    {
        var track = new Track { Id = "t", Title = "hi-res", AlbumId = "a", ArtistId = "r", SampleRate = 192000, BitDepth = 24 };
        var cap = new OutputCapability { PeerId = "o", DisplayName = "48k dac", MaxSampleRate = 48000, MaxBitDepth = 24 };
        var room = ListeningRoom.ForMode("r", "n", "h", RoomMode.OpenLounge);
        room.QualityPolicy = QualityPolicy.LowestCommonFormat;

        var decision = QualityPolicyEngine.Evaluate(room, track, cap);
        Assert.True(decision.CanHear);
        Assert.True(decision.NeedSrc);
        Assert.Contains("SRC", decision.Badge);
        Assert.Equal((48000, 24), QualityPolicyEngine.CommonFormat([cap], track));
    }

    [Fact]
    public void PlayRefusesWhenNoOutputCanDoBitPerfect()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "Quiet", RoomMode.Audiophile, "H");
        rooms.Enqueue(room.Id, "host", "tr-blue-train");   // 24/96
        rooms.RegisterOutput(room.Id, new OutputCapability
        {
            PeerId = "cd-only",
            DisplayName = "16/44 only",
            MaxSampleRate = 44100,
            MaxBitDepth = 16
        });

        var play = rooms.Play(room.Id, "host");
        Assert.Equal("no bit-perfect capable output", play.Error);
    }

    [Fact]
    public void DjHandoffIsAtomic()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "DJ", RoomMode.HostQueue, "H");
        rooms.Join(room.Id, "b", PeerRole.Control, null, "B");
        var t = rooms.TransferHost(room.Id, "host", "b");
        Assert.Equal("b", t.Room!.HostPeerId);
        Assert.Equal(MemberRole.Host, t.Room.RoleOf("b"));
        Assert.Equal(MemberRole.Listener, t.Room.RoleOf("host"));
    }

    [Fact]
    public void ClockReportSurfacesInSnapshot()
    {
        var (rooms, _, _, _) = NewStack();
        var room = rooms.Create("host", "Lounge", RoomMode.OpenLounge, "H");
        rooms.RegisterOutput(room.Id, new OutputCapability { PeerId = "dac", DisplayName = "DAC" });
        rooms.ReportClock(room.Id, "dac", offsetMs: 1.5, jitterMs: 0.3, rttMs: 2, bufferMs: 6, resyncs: 0, locked: true);

        var json = rooms.SnapshotJson(rooms.Get(room.Id)!);
        Assert.Contains("\"offsetMs\":1.5", json);
        Assert.Contains("\"bufferMs\":6", json);
    }
}

public class TransportAndArchiveTests
{
    [Fact]
    public void MatpFrameRoundTripsEncryptedPayloadWithEpoch()
    {
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var pcm = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = MatpFrame.Encode(new MatpAudio(12000, 96000, 24, 2, false, 7, pcm), key);

        Assert.True(MatpFrame.TryDecode(frame, key, out var decoded));
        Assert.Equal(12000, decoded.PtsMs);
        Assert.Equal(96000, decoded.SampleRate);
        Assert.Equal(7, decoded.Epoch);
        Assert.Equal(pcm, decoded.Payload);
        // 키가 없으면 페이로드를 열 수 없다.
        Assert.False(MatpFrame.TryDecode(frame, null, out _));
    }

    [Fact]
    public void DsdFramesKeepTheirNativeFlag()
    {
        var frame = MatpFrame.Encode(new MatpAudio(0, 2822400, 1, 2, true, 0, [0xAA, 0x55]), null);
        Assert.True(MatpFrame.TryDecode(frame, null, out var decoded));
        Assert.True(decoded.IsDsd);
        Assert.Equal(new byte[] { 0xAA, 0x55 }, decoded.Payload);
    }

    [Fact]
    public void PlayAtIsTheTimelineOriginPlusMediaOffset()
    {
        var origin = 1_700_000_000_000L;
        var playAt = ClockSync.PlayAtUnixMs(origin, mediaTimeAtOriginMs: 5_000, mediaTimeMs: 5_020, outputLatencyMs: 10);
        Assert.Equal(origin + 30, playAt);
    }

    [Fact]
    public void WavSlicerReadsRealPcmAtMediaTime()
    {
        var path = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n") + ".wav");
        WriteWav(path, sampleRate: 48000, channels: 2, bits: 16, seconds: 1);
        try
        {
            using var slicer = new WavSlicer(path);
            Assert.Equal(48000, slicer.Format.SampleRate);
            Assert.Equal(2, slicer.Format.Channels);
            Assert.InRange(slicer.DurationMs, 990, 1010);

            var chunk = slicer.Read(500, 20);
            Assert.Equal(48000 / 1000 * 20 * 4, chunk.Length);
            Assert.Empty(slicer.Read(5_000, 20));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PlaylistExportsAsM3u()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var history = new HistoryStore(Path.Combine(dir, "h.db"));

        var playlist = history.CreatePlaylist("Night", ["tr-blue-train", "tr-so-what"], "host");
        var m3u = history.ExportM3u(playlist, catalog);

        Assert.StartsWith("#EXTM3U", m3u);
        Assert.Contains("John Coltrane - Blue Train", m3u);
        Assert.Contains("tr-so-what", m3u);
        Assert.Equal(playlist.Id, history.Playlist(playlist.Id)!.Id);
    }

    [Fact]
    public void SessionSummarySharesIdentifiersNotFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var history = new HistoryStore(Path.Combine(dir, "h.db"));
        var archive = history.SaveArchive(new SessionArchive
        {
            Id = "a1",
            RoomId = "r1",
            RoomName = "Night Lounge",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            EndedAt = DateTimeOffset.UtcNow,
            TrackIds = ["tr-blue-train"],
            HighlightTrackIds = ["tr-blue-train"],
            Pins = [new TimestampPin("p1", "host", "tr-blue-train", 12000, "horn", DateTimeOffset.UtcNow)]
        });

        var summary = history.ShareSummary(archive, catalog);
        Assert.Contains("Night Lounge", summary);
        Assert.Contains("[tr-blue-train]", summary);
        Assert.Contains("horn", summary);
        Assert.DoesNotContain(".wav", summary);
    }

    [Fact]
    public void RetentionSweepDropsExpiredComments()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var history = new HistoryStore(Path.Combine(dir, "h.db"));
        var old = DateTimeOffset.UtcNow.AddDays(-100);
        history.SaveArchive(new SessionArchive
        {
            Id = "a1",
            RoomId = "r1",
            RoomName = "Old",
            EndedAt = old,
            StartedAt = old,
            RetentionDays = 30,
            TrackIds = ["tr-blue-train"],
            Pins = [new TimestampPin("p1", "host", "tr-blue-train", 1000, "stale", old)],
            Chat = [new ChatLine("host", "stale", old)]
        });

        Assert.Equal(2, history.PruneComments(DateTimeOffset.UtcNow));
        var swept = history.Archive("a1")!;
        Assert.Empty(swept.Pins);
        Assert.Empty(swept.Chat);
        Assert.Single(swept.TrackIds);
    }

    private static void WriteWav(string path, int sampleRate, int channels, int bits, int seconds)
    {
        var frames = sampleRate * seconds;
        var bytesPerFrame = channels * (bits / 8);
        var data = new byte[frames * bytesPerFrame];
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + data.Length);
        bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * bytesPerFrame);
        bw.Write((short)bytesPerFrame);
        bw.Write((short)bits);
        bw.Write("data"u8.ToArray());
        bw.Write(data.Length);
        bw.Write(data);
    }
}
