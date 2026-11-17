using Mono.Core;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 화면의 "샘플레이트 변환(SRC)" 표시는 실제로 변환이 일어날 때만 켜져야 한다.
///
/// 헤드폰·스피커 프리셋은 2배 업샘플링을 함께 하지만, 클럭싱크 룸에서는 출력 워커가 장치가
/// 2배 레이트를 받을 때만 올린다. 예전에는 프리셋만 보고 SRC 를 켜서, 올리지 않고 원래 레이트로
/// 내보내는 동안에도 화면은 변환했다고 말했다.
/// </summary>
public class SignalPathTruthTests
{
    private static (RoomManager Rooms, ListeningRoom Room) Playing(RoomMode mode, int trackRate)
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
        catalog.UpsertTrack(
            new Track
            {
                Id = "tr-1", Title = "Track", AlbumId = album.Id, ArtistId = artist.Id,
                DurationMs = 240_000, SampleRate = trackRate, BitDepth = 24
            },
            album,
            artist);

        var room = rooms.Create("me", "방", mode, "Listener");
        Assert.Null(rooms.PlayList(room.Id, "me", ["tr-1"], 0).Error);
        return (rooms, room);
    }

    private static void Attach(RoomManager rooms, ListeningRoom room, params int[] rates)
        => rooms.RegisterOutput(room.Id, new OutputCapability
        {
            PeerId = "out-1",
            DisplayName = "DAC",
            MaxSampleRate = rates.Length > 0 ? rates.Max() : 192_000,
            MaxBitDepth = 24,
            SampleRates = [.. rates]
        });

    private static void Preset(RoomManager rooms, ListeningRoom room, DspPresetKind preset)
        => rooms.ApplyHostSettings(room.Id, "me", r =>
        {
            r.DspPreset = preset;
            r.DspEnabled = preset != DspPresetKind.Off;
        });

    [Fact]
    public void UpsamplingPresetShowsSrcWhenTheDeviceTakesTheDoubledRate()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        Attach(rooms, room, 44_100, 48_000, 88_200, 96_000);

        Preset(rooms, room, DspPresetKind.Headphones);

        Assert.True(room.SrcApplied);
        Assert.False(room.BitPerfect);
    }

    /// <summary>장치가 88.2kHz 를 받지 않으면 워커는 올리지 않고 44.1kHz 로 낸다. 변환했다고 말하면 안 된다.</summary>
    [Fact]
    public void UpsamplingPresetDoesNotClaimSrcWhenTheDeviceCannotTakeIt()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        Attach(rooms, room, 44_100, 48_000);

        Preset(rooms, room, DspPresetKind.Headphones);

        Assert.False(room.SrcApplied);
        // DSP 는 걸려 있으므로 비트퍼펙트는 여전히 아니다.
        Assert.False(room.BitPerfect);
    }

    /// <summary>최댓값만으로는 알 수 없다. 96kHz 까지 받는 장치가 88.2kHz 는 받지 않을 수 있다.</summary>
    [Fact]
    public void TheRateListDecidesNotTheMaximum()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        Attach(rooms, room, 44_100, 48_000, 96_000);

        Preset(rooms, room, DspPresetKind.Speakers);

        Assert.False(room.SrcApplied);
    }

    /// <summary>192kHz 곡은 384kHz 로 올릴 수 없다. 같은 장치에서 96kHz 곡은 올린다.</summary>
    [Theory]
    [InlineData(96_000, true)]
    [InlineData(192_000, false)]
    public void WhetherItUpsamplesDependsOnTheTrack(int trackRate, bool expected)
    {
        var (rooms, room) = Playing(RoomMode.Solo, trackRate);
        Attach(rooms, room, 44_100, 48_000, 88_200, 96_000, 176_400, 192_000);

        Preset(rooms, room, DspPresetKind.Headphones);

        Assert.Equal(expected, room.SrcApplied);
    }

    /// <summary>샘플레이트 목록을 보내지 않는 옛 워커는 최댓값으로 짐작한다.</summary>
    [Fact]
    public void AnOutputThatReportsNoListIsJudgedByItsMaximum()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        rooms.RegisterOutput(room.Id, new OutputCapability
        {
            PeerId = "out-1", DisplayName = "DAC", MaxSampleRate = 96_000, MaxBitDepth = 24
        });

        Preset(rooms, room, DspPresetKind.Headphones);

        Assert.True(room.SrcApplied);
    }

    [Fact]
    public void WithNoOutputNothingIsConverted()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);

        Preset(rooms, room, DspPresetKind.Headphones);

        Assert.False(room.SrcApplied);
    }

    /// <summary>업샘플링을 하지 않는 프리셋은 장치가 무엇을 받든 SRC 가 아니다.</summary>
    [Theory]
    [InlineData(DspPresetKind.Parametric)]
    [InlineData(DspPresetKind.Crossfeed)]
    [InlineData(DspPresetKind.RoomIr)]
    [InlineData(DspPresetKind.Off)]
    public void OtherPresetsNeverShowSrc(DspPresetKind preset)
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        Attach(rooms, room, 44_100, 48_000, 88_200, 96_000);

        Preset(rooms, room, preset);

        Assert.False(room.SrcApplied);
    }

    /// <summary>팬아웃 룸에서는 Core 가 올려서 보낸다. 장치 목록과 무관하게 변환이 일어난다.</summary>
    [Fact]
    public void FanOutRoomsUpsampleInCore()
    {
        var (rooms, room) = Playing(RoomMode.OpenLounge, 44_100);
        Attach(rooms, room, 44_100, 48_000);

        Preset(rooms, room, DspPresetKind.Headphones);

        Assert.True(room.SrcApplied);
    }

    /// <summary>나중에 붙은 장치도 반영된다. 표시는 장치가 등록되는 순간 다시 계산한다.</summary>
    [Fact]
    public void AttachingADeviceRefreshesTheVerdict()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        Preset(rooms, room, DspPresetKind.Headphones);
        Assert.False(room.SrcApplied);

        Attach(rooms, room, 44_100, 88_200);

        Assert.True(room.SrcApplied);
    }

    // ── 하지 않은 변환, 아닌 비트퍼펙트 ──────────────────────────────────────

    /// <summary>
    /// 장치가 곡의 규격을 받지 못할 때. Mono 에는 규격을 맞춰 주는 리샘플러가 없으므로 변환했다고
    /// 하지 않는다. 그렇다고 비트퍼펙트도 아니다 — 재생되지 않거나, 공유 모드에서 Windows 가 변환한다.
    /// </summary>
    [Fact]
    public void ADeviceThatCannotTakeTheFormatIsNeitherConvertedNorBitPerfect()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 192_000);
        Attach(rooms, room, 44_100, 48_000);

        Assert.False(room.SrcApplied);
        Assert.False(room.BitPerfect);
        Assert.DoesNotContain("SRC", room.PathBadge);
        Assert.DoesNotContain("Bit-perfect", room.PathBadge);
    }

    [Fact]
    public void ACapableExclusiveDeviceAtFullVolumeIsBitPerfect()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 96_000);
        Attach(rooms, room, 44_100, 48_000, 88_200, 96_000);

        Assert.True(room.BitPerfect);
        Assert.False(room.SrcApplied);
        Assert.Contains("Bit-perfect", room.PathBadge);
    }

    /// <summary>배타가 거절되어 공유로 내려간 것은 워커의 보고로만 안다. 보고가 오면 표시가 내려간다.</summary>
    [Fact]
    public void FallingBackToSharedModeEndsBitPerfect()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        Attach(rooms, room, 44_100, 48_000);
        Assert.True(room.BitPerfect);

        rooms.ReportClock(room.Id, "out-1", 0, 0, 1, 100, 0, true, deviceState: 3);
        Assert.False(room.BitPerfect);

        rooms.ReportClock(room.Id, "out-1", 0, 0, 1, 100, 0, true, deviceState: 2);
        Assert.True(room.BitPerfect);
    }

    [Fact]
    public void AnOutputStartedInSharedModeIsNotBitPerfect()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        rooms.RegisterOutput(room.Id, new OutputCapability
        {
            PeerId = "out-1", DisplayName = "DAC", MaxSampleRate = 48_000, MaxBitDepth = 24, ExclusiveMode = false
        });

        Assert.False(room.BitPerfect);
    }

    /// <summary>하드웨어 볼륨이 없는 장치(ASIO)에서 볼륨을 줄이면 샘플이 바뀐다. 100% 로 돌아오면 다시 그대로다.</summary>
    [Fact]
    public void DigitalVolumeEndsBitPerfect()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        Attach(rooms, room, 44_100, 48_000);

        rooms.SetVolume(room.Id, "me", "out-1", 60);
        Assert.False(room.BitPerfect);

        rooms.SetVolume(room.Id, "me", "out-1", 100);
        Assert.True(room.BitPerfect);
    }

    [Fact]
    public void HardwareVolumeKeepsBitPerfect()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        rooms.RegisterOutput(room.Id, new OutputCapability
        {
            PeerId = "out-1", DisplayName = "DAC", MaxSampleRate = 48_000, MaxBitDepth = 24, HardwareVolume = true
        });

        rooms.SetVolume(room.Id, "me", "out-1", 60);

        Assert.True(room.BitPerfect);
    }

    // ── 지연과 드라이버 ──────────────────────────────────────────────────────

    /// <summary>
    /// 워커는 접속할 때 장치를 열기 전이라 지연을 모른다. 모르는 동안은 0 이고(화면은 표시하지 않는다),
    /// 장치를 연 뒤의 보고로 실제 값이 들어온다.
    /// </summary>
    [Fact]
    public void LatencyIsUnknownUntilTheDeviceReportsIt()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        Attach(rooms, room, 44_100, 48_000);
        Assert.Equal(0, room.Outputs["out-1"].ReportedLatencyMs);

        rooms.ReportClock(room.Id, "out-1", 0, 0, 1, 100, 0, true, deviceState: 2, latencyMs: 12);
        Assert.Equal(12, room.Outputs["out-1"].ReportedLatencyMs);

        // 값을 싣지 않은 보고가 알고 있던 값을 지우지 않는다.
        rooms.ReportClock(room.Id, "out-1", 0, 0, 1, 100, 0, true, deviceState: 2);
        Assert.Equal(12, room.Outputs["out-1"].ReportedLatencyMs);
    }

    [Fact]
    public void TheSnapshotSaysWhichDriverTheOutputUses()
    {
        var (rooms, room) = Playing(RoomMode.Solo, 44_100);
        rooms.RegisterOutput(room.Id, new OutputCapability
        {
            PeerId = "out-1", DisplayName = "Babyface", MaxSampleRate = 192_000, MaxBitDepth = 24, Driver = "ASIO"
        });

        using var doc = System.Text.Json.JsonDocument.Parse(rooms.SnapshotJson(room));
        var output = doc.RootElement.GetProperty("outputs")[0];

        Assert.Equal("ASIO", output.GetProperty("driver").GetString());
    }
}
