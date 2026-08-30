using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 라이선스 분기의 어댑터 경계.
/// ClockSync = 각 참가자가 자기 라이선스로 재생, Core는 타임라인만 방송(데이터 플레인 없음).
/// FanOut    = Core가 디코드한 청크를 암호화해 Output에 보낸다(로컬 파일/개인 라운지).
/// </summary>
public interface IPlaybackSource : IDisposable
{
    PlaybackSourceMode Mode { get; }
    Track Track { get; }
    bool ProducesDataPlane { get; }
    AudioFormat Format { get; }
    long DurationMs { get; }
    byte[] Read(long startMs, int durationMs);
}

public sealed class ClockSyncPlaybackSource : IPlaybackSource
{
    public ClockSyncPlaybackSource(Track track)
    {
        Track = track;
        Format = new AudioFormat(track.SampleRate, track.BitDepth, Math.Max(track.Channels, 2), track.IsDsd);
        DurationMs = track.DurationMs;
    }

    public PlaybackSourceMode Mode => PlaybackSourceMode.ClockSync;
    public Track Track { get; }
    public bool ProducesDataPlane => false;
    public AudioFormat Format { get; }
    public long DurationMs { get; }

    /// <summary>파일을 팬아웃하지 않는다. 각 엔드포인트가 같은 track_id를 자기 소스로 연다.</summary>
    public byte[] Read(long startMs, int durationMs) => [];

    public void Dispose() { }
}

public sealed class FanOutPlaybackSource : IPlaybackSource
{
    private readonly ITrackSlicer _slicer;

    public FanOutPlaybackSource(Track track)
    {
        Track = track;
        _slicer = AudioSourceFactory.Open(track);
        DurationMs = track.DurationMs > 0 ? track.DurationMs : _slicer.DurationMs;
    }

    public PlaybackSourceMode Mode => PlaybackSourceMode.FanOut;
    public Track Track { get; }
    public bool ProducesDataPlane => true;
    public AudioFormat Format => _slicer.Format;
    public long DurationMs { get; }

    public byte[] Read(long startMs, int durationMs) => _slicer.Read(startMs, durationMs);

    public void Dispose() => _slicer.Dispose();
}

public static class PlaybackSourceFactory
{
    /// <summary>
    /// 스트리밍 트랙은 팬아웃 대상이 아니다. 룸이 Fan-out이어도 소스가 스트리밍이면
    /// 자동으로 Clock-sync(각자 재생 + 클럭만 동기)로 내려간다.
    /// </summary>
    public static IPlaybackSource For(Track track, PlaybackSourceMode mode)
        => mode == PlaybackSourceMode.FanOut && track.Source == StreamingProvider.Local
            ? new FanOutPlaybackSource(track)
            : new ClockSyncPlaybackSource(track);
}
