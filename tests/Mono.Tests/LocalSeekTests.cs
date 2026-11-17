using Mono.Output;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 재생 중에 탐색하면 소리가 뚝뚝 끊기던 문제(Clock-sync 로컬 재생, 혼자 듣기의 기본 경로).
///
/// 출력 워커는 15ms 마다 "타임라인의 지금 자리"를 달라고 디코더를 불렀고, 디코더는 지난번에
/// 불린 자리에서 80ms 넘게 벌어지면 탐색으로 보고 자리를 다시 잡았다. 그런데 탐색 직후의
/// 첫 읽기는 디코더가 자리를 찾느라 그 80ms 를 쉽게 넘긴다 — 그러면 다음 호출도 "멀리 뛰었다"가
/// 되어 또 탐색하고, 한 번 읽은 디코더의 탐색은 리더를 통째로 새로 여는 일이라 더 오래 걸린다.
/// 그렇게 30ms 조각 하나를 낼 때마다 디코더를 다시 여는 고리가 돌았다.
/// </summary>
public class LocalSeekTests
{
    private const int Rate = 44100;

    private static long Frames(long ms) => ms * Rate / 1000;

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "seek-markers.flac");

    /// <summary>24비트 PCM 의 첫 샘플. 픽스처는 n초대의 값이 (n+1)/8 로 고정돼 있다.</summary>
    private static double FirstSample(byte[] pcm)
    {
        Assert.True(pcm.Length >= 3);
        return (pcm[0] | pcm[1] << 8 | (sbyte)pcm[2] << 16) / 8388608.0;
    }

    [Fact]
    public void AFreshDecoderMustBePlaced()
    {
        Assert.True(RenderGate.LocalCursorLost(-1, Frames(60_000), Rate));
    }

    /// <summary>
    /// 이어 재생 중에는 디코더가 장치 버퍼 깊이만큼 타임라인보다 앞서 있다. 그건 탐색이 아니다.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(140)]
    [InlineData(290)]
    public void RunningAheadByTheBufferIsNotASeek(int aheadMs)
    {
        var timeline = Frames(60_000);
        Assert.False(RenderGate.LocalCursorLost(timeline + Frames(aheadMs), timeline, Rate));
    }

    /// <summary>
    /// 탐색 직후 첫 읽기가 120ms 걸렸다. 예전 기준(80ms)으로는 여기서 다시 탐색해 고리가 시작됐다.
    /// 이제는 그대로 이어 읽는다.
    /// </summary>
    [Fact]
    public void ASlowFirstReadDoesNotTriggerAnotherSeek()
    {
        var seekTarget = Frames(60_000);
        var nextFrame = seekTarget + Frames(30);          // 30ms 조각을 읽고 난 디코더
        var timelineNow = seekTarget + Frames(120 + 15);  // 읽기 120ms + 루프 한 바퀴

        Assert.False(RenderGate.LocalCursorLost(nextFrame, timelineNow, Rate));
    }

    /// <summary>한참 뒤처졌으면 그 사이를 건너뛰어 따라잡는다.</summary>
    [Fact]
    public void FallingFarBehindCatchesUp()
    {
        var seekTarget = Frames(60_000);
        var nextFrame = seekTarget + Frames(30);
        var timelineNow = seekTarget + Frames(400);

        Assert.True(RenderGate.LocalCursorLost(nextFrame, timelineNow, Rate));
    }

    /// <summary>타임라인이 디코더보다 훨씬 뒤에 있으면 되감긴 것이다.</summary>
    [Fact]
    public void ATimelineFarBehindTheDecoderIsARewind()
    {
        var timeline = Frames(60_000);
        Assert.True(RenderGate.LocalCursorLost(timeline + Frames(RenderGate.LocalAheadLimitMs + 50), timeline, Rate));
    }

    /// <summary>
    /// 늦게 읽혀서 버리는 조각은, 버리지 않았어도 다음 읽기가 건너뛰었을 조각이어야 한다.
    /// 두 기준이 어긋나면 조각을 버려 놓고 그 뒤를 그대로 이어 읽어 앞부분만 잃는다.
    /// </summary>
    [Theory]
    [InlineData(40, 30)]
    [InlineData(170, 30)]
    [InlineData(190, 30)]
    [InlineData(600, 30)]
    public void AChunkIsDroppedOnlyWhenTheNextReadWouldSkipAhead(int readMs, int chunkMs)
    {
        var start = Frames(60_000);
        var nextFrame = start + Frames(chunkMs);
        var timelineNow = start + Frames(readMs);

        Assert.Equal(
            RenderGate.LocalCursorLost(nextFrame, timelineNow, Rate),
            RenderGate.IsStaleLocalRead(readMs, chunkMs));
    }

    /// <summary>
    /// 다음 호출이 110ms 늦게 와도 디코더는 끊긴 데 없이 이어서 내준다.
    /// 예전에는 여기서 다시 탐색해 1초대의 소리가 났다.
    /// </summary>
    [Fact]
    public void ALateCallContinuesFromWhereTheDecoderStopped()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var source = LocalFileRenderer.TryOpen(Fixture);
        Assert.NotNull(source);

        Assert.InRange(FirstSample(source.Read(900, 30, out _)), 0.124, 0.126);

        // 디코더는 0.93초에 서 있고 타임라인은 1.04초다. 이어 읽으면 아직 0초대의 소리다.
        Assert.InRange(FirstSample(source.Read(1040, 30, out _)), 0.124, 0.126);
    }

    /// <summary>
    /// 많이 뒤처지면 지금 자리로 따라잡는다. 디코더를 새로 열지 않고 사이를 버리는 경로이므로,
    /// 건너뛴 뒤의 자리가 정확한지를 여기서 확인한다.
    /// </summary>
    [Fact]
    public void CatchingUpLandsOnTheRequestedAudio()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var source = LocalFileRenderer.TryOpen(Fixture);
        Assert.NotNull(source);

        Assert.InRange(FirstSample(source.Read(900, 30, out _)), 0.124, 0.126);
        Assert.InRange(FirstSample(source.Read(1300, 30, out _)), 0.249, 0.251);
        Assert.InRange(FirstSample(source.Read(2600, 30, out _)), 0.374, 0.376);

        // 되감기는 건너뛸 수 없다 — 새로 자리를 잡아야 한다.
        Assert.InRange(FirstSample(source.Read(500, 30, out _)), 0.124, 0.126);
    }
}
