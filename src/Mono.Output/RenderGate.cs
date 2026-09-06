namespace Mono.Output;

/// <summary>
/// 렌더 버퍼 깊이 제어 판정.
///
/// 지연을 줄이겠다고 오디오 프레임을 버리면 그 자리에 파형 불연속이 남아 딸깍(클릭) 소리가 난다.
/// 연속으로 버리면 클릭이 이어져 지지직거리는 잡음이 된다. 그래서 이 게이트는 "버릴지"가 아니라
/// "지금 넣을지 미룰지"만 정한다 — 미룬 프레임은 큐에 남아 다음 루프에서 온전히 들어간다.
/// </summary>
public static class RenderGate
{
    /// <summary>렌더 루프가 깨어나는 간격. Windows 기본 타이머 분해능이 ~16ms다.</summary>
    public const int WakeSlackMs = 16;

    /// <summary>이보다 늦게 도착한 프레임만 버린다. 조금 늦으면 늦게라도 내보내는 편이 구멍보다 낫다.</summary>
    public const int LateToleranceMs = 250;

    /// <summary>청크 길이를 Core 의 상수를 짐작하지 않고 프레임 자체에서 잰다.</summary>
    public static double FrameDurationMs(int payloadBytes, int sampleRate, int bitDepth, int channels, bool isDsd)
    {
        if (sampleRate <= 0 || payloadBytes <= 0) return 0;
        var ch = Math.Max(1, channels);

        // DSD는 1바이트에 8샘플이 들어간다.
        if (isDsd) return payloadBytes / ch * 8.0 * 1000.0 / sampleRate;

        var bytesPerFrame = ch * Math.Max(1, bitDepth / 8);
        return payloadBytes / bytesPerFrame * 1000.0 / sampleRate;
    }

    /// <summary>
    /// 버퍼가 도달해도 되는 최대 깊이. 청크 하나와 루프가 깨어나는 간격을 모두 담지 못하면
    /// 정상 재생에서도 매번 천장에 걸려, 지연을 줄이려다 오디오를 잘라먹게 된다.
    /// </summary>
    public static double DepthCeilingMs(double targetBufferMs, double rendererLatencyMs, double frameMs)
        => targetBufferMs + rendererLatencyMs + frameMs + WakeSlackMs;

    /// <summary>지금은 넣지 말고 큐에 두어야 하는가(백프레셔).</summary>
    public static bool ShouldHold(double bufferedMs, double ceilingMs) => bufferedMs > ceilingMs;

    /// <summary>백프레셔로 감당할 수 없을 만큼 벌어져 버퍼를 비워야 하는가.</summary>
    public static bool ShouldFlush(double bufferedMs, double ceilingMs) => bufferedMs > ceilingMs + 200;
}
