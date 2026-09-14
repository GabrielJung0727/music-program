using Mono.Protocol;

namespace Mono.Control.Services;

/// <summary>재생 트랜스포트와 동기화. 오디오는 Output이 내고 Control은 명령만 보낸다.</summary>
public sealed partial class CoreSession
{
    public Task PlayAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Play });
    public Task PauseAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Pause });
    public Task SkipAsync(int delta = 1) => SendAsync(new MonoMessage { Type = MessageTypes.Skip, Index = delta });
    public Task SeekAsync(long ms) => SendAsync(new MonoMessage { Type = MessageTypes.Seek, MediaTimeMs = ms });
    public Task SyncProbeAsync() => SendAsync(new MonoMessage { Type = MessageTypes.SyncProbe });
    public Task ResyncAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Resync });
    public Task ChooseAutoplayAsync(string trackId) => SendAsync(new MonoMessage { Type = MessageTypes.ChooseAutoplay, TrackId = trackId });

    /// <summary>
    /// 출력 기기 볼륨. targetPeerId를 비우면 Core가 보낸 쪽을 대상으로 삼는다.
    /// 하드웨어 볼륨이 있으면 Core가 그쪽을 쓰고, Bit-perfect 정책이 디지털 감쇠를
    /// 막으면 error로 돌아온다.
    /// </summary>
    public Task SetVolumeAsync(string? targetPeerId, int percent)
        => SendAsync(new MonoMessage
        {
            Type = MessageTypes.SetVolume,
            TargetPeerId = targetPeerId,
            Volume = Math.Clamp(percent, 0, 100)
        });

    /// <summary>원격 Control 페어링 코드 발급 요청. 응답은 pairing_issued다.</summary>
    public Task PairAsync()
        => SendAsync(new MonoMessage { Type = MessageTypes.Pair });

    public Task RedeemAsync(string pairingCode)
        => SendAsync(new MonoMessage { Type = MessageTypes.Redeem, PairingCode = pairingCode });
}
