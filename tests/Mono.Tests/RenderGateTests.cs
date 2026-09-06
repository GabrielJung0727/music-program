using System.Linq;
using Mono.Output;
using Mono.Protocol;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 렌더 버퍼 깊이 제어 회귀 테스트.
///
/// 증상: 재생 중 "지지지직" 잡음과 "탁 타타탁" 딸깍 소리. 원인은 디코딩이 아니라 렌더 경로였다 —
/// (1) 깊이 제어가 오디오 프레임을 통째로 버려 파형 불연속을 만들었고,
/// (2) 빈 버퍼로 재생을 시작해 정상 재생 중에도 계속 언더런이 났다.
/// 두 결함 모두 20ms 청크 하나조차 담지 못하는 좁은 버퍼 설정에서 비롯됐다.
/// </summary>
public class RenderGateTests
{
    private const int ChunkMs = 20;

    /// <summary>Windows 기본 타이머 분해능. 렌더 루프는 이 간격으로만 깨어난다.</summary>
    private const int WakeMs = 16;

    private sealed record SimResult(int UnderrunMs, int DeletedFrames, double MaxBufferedMs);

    /// <summary>
    /// 렌더 루프를 1ms 간격으로 모사한다. Core 는 20ms 청크를 실시간으로 만들고,
    /// 렌더 루프는 16ms마다 깨어나며, DAC 은 1ms당 1ms를 소비한다.
    /// </summary>
    private static SimResult Simulate(
        int targetBufferMs,
        int latencyMs,
        int primeMs,
        double ceilingMs,
        bool backPressure,
        int durationMs = 30_000)
    {
        double buffered = 0;
        var playing = false;
        var underrun = 0;
        var deleted = 0;
        var maxBuffered = 0.0;
        var nextPts = 0;      // 아직 보내지 않은 가장 오래된 청크의 PTS
        var producedTo = 0;   // Core 가 만들어 둔 지점

        for (var t = 0; t <= durationMs; t++)
        {
            // Core 는 실시간으로 청크를 만든다.
            while (producedTo <= t) producedTo += ChunkMs;

            if (t % WakeMs == 0)
            {
                // PTS 게이트: 청크는 pts + 지터버퍼 시점에 방출 자격을 얻는다.
                while (nextPts < producedTo && nextPts + targetBufferMs <= t)
                {
                    if (playing && buffered > ceilingMs)
                    {
                        if (backPressure) break;   // 큐에 두고 다음 루프에 다시 시도
                        deleted++;                 // 예전 동작: 오디오를 버린다
                        nextPts += ChunkMs;
                        continue;
                    }

                    buffered += ChunkMs;
                    nextPts += ChunkMs;
                    if (!playing && buffered >= primeMs) playing = true;
                }
            }

            maxBuffered = Math.Max(maxBuffered, buffered);

            if (playing)
            {
                buffered -= 1;
                if (buffered <= 0)
                {
                    buffered = 0;
                    underrun++;   // 버퍼가 비었다 — 이 1ms는 무음(=파형 구멍)이다
                }
            }
        }

        return new SimResult(underrun, deleted, maxBuffered);
    }

    /// <summary>
    /// 고친 설정에서는 오디오를 한 프레임도 버리지 않고 언더런도 나지 않는다.
    /// 버퍼 깊이는 천장 안에 머물러 지연이 자라지도 않는다.
    /// </summary>
    [Fact]
    public void SteadyPlaybackNeitherDropsAudioNorUnderruns()
    {
        var target = ClockSync.JitterBufferMs(0.1, 0.05);   // 루프백: 하한값
        const int latency = 10;                              // WASAPI Exclusive
        var ceiling = RenderGate.DepthCeilingMs(target, latency, ChunkMs);

        var r = Simulate(target, latency, primeMs: target, ceiling, backPressure: true);

        Assert.Equal(0, r.DeletedFrames);
        Assert.Equal(0, r.UnderrunMs);
        Assert.True(r.MaxBufferedMs <= ceiling + ChunkMs,
            $"버퍼가 천장을 넘었다: {r.MaxBufferedMs:F0}ms > {ceiling + ChunkMs:F0}ms");
    }

    /// <summary>
    /// 예전 설정(지터버퍼 5ms · 천장 target+지연+10ms · 빈 버퍼로 즉시 재생)은
    /// 같은 조건에서 오디오를 버리거나 언더런을 낸다. 이 대비가 없으면 위 테스트는
    /// 회귀를 잡지 못하고 그냥 통과만 한다.
    /// </summary>
    [Fact]
    public void OldNarrowBufferProducedTheGapsThatWereHeardAsNoise()
    {
        const int oldTarget = 5;     // 예전 JitterBufferMs 하한
        const int latency = 10;
        const double oldCeiling = oldTarget + latency + 10;   // 청크(20ms)보다 좁다

        var r = Simulate(oldTarget, latency, primeMs: 0, oldCeiling, backPressure: false);

        Assert.True(r.UnderrunMs + r.DeletedFrames > 0,
            "예전 설정이 멀쩡했다면 근본 원인 진단이 틀린 것이다");
    }

    /// <summary>천장은 반드시 청크 하나와 웨이크 퀀텀을 함께 담아야 한다.</summary>
    [Fact]
    public void DepthCeilingLeavesRoomForOneChunkAndOneWake()
    {
        var target = ClockSync.JitterBufferMs(0, 0);
        var ceiling = RenderGate.DepthCeilingMs(target, 10, ChunkMs);
        Assert.True(ceiling - target >= ChunkMs + RenderGate.WakeSlackMs,
            $"천장 여유 {ceiling - target:F0}ms 가 청크+웨이크({ChunkMs + RenderGate.WakeSlackMs}ms)보다 좁다");
    }

    /// <summary>
    /// 회귀: 곡을 넘기면 Core 는 새 곡의 룩어헤드를 한 번에 보낸다(실측 13프레임 · PTS 7~247ms).
    /// 수신 루프는 이미 새 epoch 로 그것들을 받아들인 뒤이므로, epoch 전환에서 큐를 통째로
    /// 비우면 새 곡의 앞부분이 통째로 사라진다.
    /// </summary>
    [Fact]
    public void EpochChangeKeepsTheIncomingTrackAndDropsOnlyTheOldOne()
    {
        var queue = new System.Collections.Concurrent.ConcurrentQueue<MatpAudio>();

        // 지난 곡의 잔여 프레임
        for (var pts = 2760L; pts <= 2820; pts += 20)
            queue.Enqueue(new MatpAudio(pts, 44100, 16, 2, false, 0, new byte[3528]));

        // 곡을 넘기자마자 도착한 새 곡의 룩어헤드 버스트
        for (var pts = 7L; pts <= 247; pts += 20)
            queue.Enqueue(new MatpAudio(pts, 96000, 24, 2, false, 1, new byte[11520]));

        var carried = RenderGate.DropStaleEpochs(queue, currentEpoch: 1);

        Assert.Empty(queue);                                  // 큐는 비워졌고
        Assert.Equal(13, carried.Count);                      // 새 곡 13프레임은 살아남았다
        Assert.All(carried, f => Assert.Equal(1, f.Epoch));
        Assert.Equal(7, carried[0].PtsMs);                    // 앞부분이 잘리지 않았다
    }

    [Fact]
    public void DropStaleEpochsKeepsArrivalOrder()
    {
        var queue = new System.Collections.Concurrent.ConcurrentQueue<MatpAudio>();
        foreach (var pts in new long[] { 40, 60, 80 })
            queue.Enqueue(new MatpAudio(pts, 48000, 24, 2, false, 5, []));

        var carried = RenderGate.DropStaleEpochs(queue, currentEpoch: 5);
        Assert.Equal(new long[] { 40, 60, 80 }, carried.Select(f => f.PtsMs));
    }

    /// <summary>청크 길이는 Core 의 상수를 짐작하지 않고 프레임에서 직접 잰다.</summary>
    [Theory]
    [InlineData(44100, 16, 2, 20)]
    [InlineData(48000, 24, 2, 20)]
    [InlineData(96000, 24, 2, 20)]
    public void FrameDurationIsMeasuredFromThePayload(int rate, int depth, int channels, int expectedMs)
    {
        var bytes = rate * expectedMs / 1000 * channels * (depth / 8);
        Assert.Equal(expectedMs, RenderGate.FrameDurationMs(bytes, rate, depth, channels, isDsd: false), 3);
    }

    [Fact]
    public void FrameDurationHandlesDsdAndDegenerateInput()
    {
        // DSD64 스테레오: 1바이트에 8샘플.
        var bytes = 2822400 / 8 * 2 * 20 / 1000;
        Assert.Equal(20, RenderGate.FrameDurationMs(bytes, 2822400, 1, 2, isDsd: true), 1);

        Assert.Equal(0, RenderGate.FrameDurationMs(0, 44100, 16, 2, false));
        Assert.Equal(0, RenderGate.FrameDurationMs(100, 0, 16, 2, false));
    }
}
