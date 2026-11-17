using System.Collections.Concurrent;
using Mono.Protocol;

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
    /// 버퍼가 도달해도 되는 최대 깊이.
    ///
    /// 아래로는 청크 하나와 루프가 깨어나는 간격을 모두 담아야 한다 — 그보다 좁으면
    /// 정상 재생에서도 매번 천장에 걸려 지연을 줄이려다 오디오를 잘라먹는다.
    /// 위로는 장치 큐 용량을 넘으면 안 된다 — 넘으면 백프레셔가 걸리기 전에 큐가 넘쳐
    /// 오디오가 조용히 버려진다(같은 결과: 파형 구멍, 딸깍 소리).
    /// </summary>
    public static double DepthCeilingMs(
        double targetBufferMs, double rendererLatencyMs, double frameMs, double capacityMs)
    {
        var wanted = targetBufferMs + rendererLatencyMs + frameMs + WakeSlackMs;

        // 큐가 넘치지 않도록 청크 하나만큼 여유를 남긴다.
        var headroom = capacityMs - frameMs;
        return headroom > 0 ? Math.Min(wanted, headroom) : wanted;
    }

    /// <summary>지금은 넣지 말고 큐에 두어야 하는가(백프레셔).</summary>
    public static bool ShouldHold(double bufferedMs, double ceilingMs) => bufferedMs > ceilingMs;

    /// <summary>백프레셔로 감당할 수 없을 만큼 벌어져 버퍼를 비워야 하는가.</summary>
    public static bool ShouldFlush(double bufferedMs, double ceilingMs) => bufferedMs > ceilingMs + 200;

    /// <summary>
    /// Clock-sync 로컬 디코더가 타임라인보다 이만큼 넘게 뒤처지면 그 사이를 건너뛰어 따라잡는다.
    /// 프라임 중이거나 드라이버가 스트림을 여는 동안에는 정상 재생에서도 수십 ms 뒤처지므로
    /// 그보다 넉넉해야 한다 — 좁으면 멀쩡한 재생 도중에 건너뛰어 딸깍 소리를 낸다.
    /// </summary>
    public const int LocalCatchUpMs = 150;

    /// <summary>
    /// 디코더가 타임라인보다 이만큼 넘게 앞서 있으면 타임라인이 되감긴 것으로 본다.
    /// 디코더는 장치 버퍼 깊이만큼 늘 앞서 있으므로 버퍼 용량보다는 커야 한다.
    /// </summary>
    public const int LocalAheadLimitMs = 500;

    /// <summary>
    /// Clock-sync 로컬 디코더가 이어 읽지 말고 요청한 자리로 옮겨 가야 하는가.
    ///
    /// 디코더의 자리는 "지난번에 부른 시각"이 아니라 디코더가 실제로 내준 프레임 수로 센다.
    /// 예전에는 부를 때마다 커서를 타임라인에 다시 맞추고 80ms 넘게 벌어지면 탐색했는데,
    /// 그러면 읽기 한 번이 오래 걸렸다는 이유만으로 탐색이 다시 걸린다. 탐색 직후의 첫 읽기가
    /// 바로 그렇게 오래 걸리고, 다시 건 탐색은 디코더를 새로 여느라 더 오래 걸려서, 30ms 조각
    /// 하나를 낼 때마다 디코더를 다시 여는 고리에 빠졌다(탐색하면 소리가 뚝뚝 끊기던 원인).
    /// </summary>
    public static bool LocalCursorLost(long nextFrame, long requestedFrame, int sampleRate)
    {
        if (nextFrame < 0) return true;

        var behind = requestedFrame - nextFrame;
        return behind > (long)LocalCatchUpMs * sampleRate / 1000
               || -behind > (long)LocalAheadLimitMs * sampleRate / 1000;
    }

    /// <summary>
    /// 방금 읽은 조각이 읽는 사이에 이미 지난 소리가 되었는가.
    ///
    /// 탐색 직후의 첫 읽기는 디코더가 자리를 찾느라 오래 걸리고, 그동안 타임라인은 흘러간다.
    /// 그 조각을 그대로 넣으면 지난 소리가 잠깐 났다가 제자리로 건너뛴다. 기준은
    /// <see cref="LocalCursorLost"/> 와 같다 — 이게 참이면 다음 읽기는 어차피 따라잡으러 건너뛴다.
    /// </summary>
    public static bool IsStaleLocalRead(double readMs, double chunkMs) => readMs - chunkMs > LocalCatchUpMs;

    /// <summary>
    /// epoch 가 바뀌었을 때 큐에서 살릴 프레임을 고른다.
    ///
    /// 곡을 넘기면 Core 는 새 곡의 룩어헤드(약 320ms)를 한 번에 보낸다. 수신 루프는 이미
    /// 새 epoch 기준으로 받아들이므로, 큐를 통째로 비우면 그 앞부분이 통째로 사라져
    /// 새 곡이 한참 뒤에서 튀어나온다. 지난 epoch 것만 버린다.
    /// </summary>
    public static List<MatpAudio> DropStaleEpochs(ConcurrentQueue<MatpAudio> queue, long currentEpoch)
    {
        var carried = new List<MatpAudio>();
        while (queue.TryDequeue(out var queued))
        {
            if (queued.Epoch == currentEpoch) carried.Add(queued);
        }

        return carried;
    }
}
