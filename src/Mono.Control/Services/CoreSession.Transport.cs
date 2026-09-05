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
}
