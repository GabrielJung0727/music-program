namespace Mono.Shared;

/// <summary>
/// media_time(ms) 을 프레임 위치로 옮기는 커서.
///
/// 청크마다 ms → 바이트를 새로 계산하면 반올림 경계에서 한 프레임이 빠지거나 겹친다.
/// 한 프레임 구멍은 파형 불연속이고, 청크마다 생기면 초당 수십 번의 딸깍 소리 —
/// 사용자 귀에는 음악 위에 얹힌 지지직거리는 잡음으로 들린다. 그래서 이어 재생 중에는
/// 프레임 커서를 그대로 이어 쓰고, 탐색으로 멀리 뛸 때만 ms 로 다시 맞춘다.
/// </summary>
public sealed class PcmFrameCursor(int sampleRate, long totalFrames)
{
    /// <summary>이 이상 벌어지면 이어 재생이 아니라 탐색으로 본다.</summary>
    private const int SeekToleranceMs = 500;

    private long _nextFrame = -1;

    public (long StartFrame, int Frames) Advance(long mediaTimeMs, int durationMs)
    {
        if (sampleRate <= 0 || totalFrames <= 0 || durationMs <= 0) return (0, 0);

        var requested = Math.Max(mediaTimeMs, 0) * sampleRate / 1000;
        var toleranceFrames = (long)SeekToleranceMs * sampleRate / 1000;
        var start = _nextFrame < 0 || Math.Abs(_nextFrame - requested) > toleranceFrames
            ? requested
            : _nextFrame;

        start = Math.Clamp(start, 0, totalFrames);
        var frames = (int)Math.Min((long)durationMs * sampleRate / 1000, totalFrames - start);
        if (frames <= 0) return (start, 0);

        _nextFrame = start + frames;
        return (start, frames);
    }

    public void Reset() => _nextFrame = -1;
}
