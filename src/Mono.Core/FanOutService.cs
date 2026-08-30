using Mono.Protocol;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// MATP 데이터 플레인 송신기. 재생 헤드보다 앞선 구간을 미리 보내고(lookahead),
/// PTS를 붙여 Output이 지정 시각에 출력하게 한다. 소셜 메시지는 이 경로에 실리지 않는다.
/// </summary>
public sealed class FanOutService : BackgroundService
{
    private const int ChunkMs = 20;
    private const int LookaheadMs = 320;
    private const int BehindToleranceMs = 400;

    private readonly RoomManager _rooms;
    private readonly CatalogStore _catalog;
    private readonly ConnectionRegistry _connections;
    private readonly ILogger<FanOutService> _log;
    private readonly Dictionary<string, RoomStream> _streams = new();

    public FanOutService(RoomManager rooms, CatalogStore catalog, ConnectionRegistry connections, ILogger<FanOutService> log)
    {
        _rooms = rooms;
        _catalog = catalog;
        _connections = connections;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var live = new HashSet<string>();
            foreach (var room in _rooms.List())
            {
                live.Add(room.Id);
                try
                {
                    await PumpAsync(room, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "fan-out room {Room}", room.Id);
                }
            }

            foreach (var stale in _streams.Keys.Where(k => !live.Contains(k)).ToList())
            {
                _streams[stale].Dispose();
                _streams.Remove(stale);
            }

            await Task.Delay(10, stoppingToken);
        }
    }

    private async Task PumpAsync(ListeningRoom room, CancellationToken ct)
    {
        if (!_streams.TryGetValue(room.Id, out var state))
        {
            state = new RoomStream();
            _streams[room.Id] = state;
        }

        var track = room.CurrentTrack(_catalog.Tracks);
        if (!room.Playing || track is null || room.SourceMode != PlaybackSourceMode.FanOut)
        {
            state.Reset();
            return;
        }

        if (state.TrackId != track.Id || state.Epoch != room.ResyncEpoch || state.Source is null)
        {
            state.Reset();
            state.Source = PlaybackSourceFactory.For(track, room.SourceMode);
            state.TrackId = track.Id;
            state.Epoch = room.ResyncEpoch;
            state.CursorMs = room.CurrentMediaTimeMs();
        }

        var source = state.Source!;
        if (!source.ProducesDataPlane)
        {
            // 스트리밍 소스 — 파일을 팬아웃하지 않고 타임라인만 유지한다.
            return;
        }

        var now = room.CurrentMediaTimeMs();
        if (state.CursorMs < now - BehindToleranceMs || state.CursorMs > now + LookaheadMs * 4)
        {
            state.CursorMs = now;
        }

        var listeners = room.OutputPeerIds
            .Where(id => !room.SpectatorPeerIds.Contains(id))
            .Where(_connections.Outputs.ContainsKey)
            .ToList();
        if (listeners.Count == 0)
        {
            state.CursorMs = now;
            return;
        }

        var duration = source.DurationMs > 0 ? source.DurationMs : track.DurationMs;
        while (state.CursorMs < now + LookaheadMs && (duration <= 0 || state.CursorMs < duration))
        {
            var pcm = source.Read(state.CursorMs, ChunkMs);
            if (pcm.Length == 0)
            {
                state.CursorMs = duration > 0 ? duration : state.CursorMs + ChunkMs;
                break;
            }

            var format = source.Format;
            var payload = pcm;
            var rate = format.SampleRate;
            var depth = format.BitDepth;
            if (!format.IsDsd)
            {
                payload = DspPipeline.Process(pcm, rate, depth, format.Channels, room, out rate, out depth, out _);
            }

            var basePts = state.CursorMs;
            byte[]? shared = null;
            foreach (var peerId in listeners)
            {
                if (!_connections.Outputs.TryGetValue(peerId, out var channel))
                {
                    continue;
                }

                var cap = room.Outputs.GetValueOrDefault(peerId);
                var needsDigitalVolume = cap is { HardwareVolume: false, VolumePercent: < 100 }
                                         && !format.IsDsd
                                         && QualityPolicyEngine.AllowsDigitalVolume(room, cap);
                byte[] frame;
                if (needsDigitalVolume)
                {
                    var attenuated = DspPipeline.ApplyGain(payload, depth, cap!.VolumePercent);
                    frame = MatpFrame.Encode(
                        new MatpAudio(basePts, rate, depth, format.Channels, format.IsDsd, room.ResyncEpoch, attenuated),
                        room.FanOutKey);
                }
                else
                {
                    shared ??= MatpFrame.Encode(
                        new MatpAudio(basePts, rate, depth, format.Channels, format.IsDsd, room.ResyncEpoch, payload),
                        room.FanOutKey);
                    frame = shared;
                }

                var line = LineFraming.Encode(new MonoMessage
                {
                    Type = MessageTypes.MatpAudio,
                    RoomId = room.Id,
                    MediaTimeMs = basePts,
                    SampleRate = rate,
                    BitDepth = depth,
                    Channels = format.Channels,
                    IsDsd = format.IsDsd,
                    Epoch = room.ResyncEpoch,
                    Body = Convert.ToBase64String(frame)
                });
                if (!await channel.SendAsync(line, ct))
                {
                    _connections.Outputs.TryRemove(peerId, out _);
                }
            }

            state.CursorMs += ChunkMs;
        }
    }

    public override void Dispose()
    {
        foreach (var stream in _streams.Values)
        {
            stream.Dispose();
        }

        _streams.Clear();
        base.Dispose();
    }

    private sealed class RoomStream : IDisposable
    {
        public IPlaybackSource? Source;
        public string TrackId = "";
        public long Epoch = -1;
        public long CursorMs;

        public void Reset()
        {
            Source?.Dispose();
            Source = null;
            TrackId = "";
            Epoch = -1;
            CursorMs = 0;
        }

        public void Dispose() => Reset();
    }
}
