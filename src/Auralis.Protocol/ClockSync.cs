namespace Auralis.Protocol;

/// <summary>
/// NTP 스타일 오프셋 추정. 가청 락(ms)과 측정 해상도(tick)를 분리한다.
/// </summary>
public static class ClockSync
{
    public static long UnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static double OffsetMs(long t1, long t2, long t3, long t4)
        => ((t2 - t1) + (t3 - t4)) / 2.0;

    public static double RttMs(long t1, long t2, long t3, long t4)
        => (t4 - t1) - (t3 - t2);

    public static double Smooth(double previous, double sample, double alpha = 0.2)
        => previous + alpha * (sample - previous);

    /// <summary>
    /// Output이 렌더할 UTC 시각. outputLatencyMs는 DAC/버퍼 지연.
    /// </summary>
    public static long PlayAtUnixMs(
        long mediaOriginUnixMs,
        long mediaTimeAtOriginMs,
        long mediaTimeMs,
        long outputLatencyMs)
        => mediaOriginUnixMs + (mediaTimeMs - mediaTimeAtOriginMs) + outputLatencyMs;

    /// <summary>
    /// 적응형 지터 버퍼 목표. LAN(저 RTT·저 지터)에서는 5ms 언저리,
    /// WAN에서는 락 유지를 위해 최대 80ms까지 늘린다.
    /// </summary>
    public static int JitterBufferMs(double rttMs, double jitterMs)
        => (int)Math.Clamp(5 + rttMs / 2 + jitterMs * 3, 5, 80);

    /// <summary>오프셋 표본의 절대 편차 평활 — RFC3550식 지터 추정의 축약형.</summary>
    public static double UpdateJitter(double jitter, double previousOffset, double offset)
        => jitter + (Math.Abs(offset - previousOffset) - jitter) / 16.0;
}
