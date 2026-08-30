using Mono.Protocol;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 재생 타임라인의 심장. 트랙이 끝나면 다음 곡으로 넘기고,
/// 주기적으로 가벼운 timeline 프레임을 뿌려 늦게 붙은 엔드포인트도 락되게 한다.
/// </summary>
public sealed class TransportService : BackgroundService
{
    private readonly RoomManager _rooms;
    private readonly RoomBroadcaster _broadcaster;
    private readonly ConnectionRegistry _connections;
    private readonly ILogger<TransportService> _log;
    private DateTimeOffset _lastTimeline = DateTimeOffset.MinValue;

    public TransportService(RoomManager rooms, RoomBroadcaster broadcaster, ConnectionRegistry connections, ILogger<TransportService> log)
    {
        _rooms = rooms;
        _broadcaster = broadcaster;
        _connections = connections;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var room in _rooms.AdvanceFinished())
                {
                    await _broadcaster.PublishAsync(room, stoppingToken);
                }

                if (DateTimeOffset.UtcNow - _lastTimeline > TimeSpan.FromSeconds(2))
                {
                    _lastTimeline = DateTimeOffset.UtcNow;
                    foreach (var room in _rooms.List().Where(r => r.Playing))
                    {
                        await SendTimelineAsync(room, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "transport tick");
            }

            await Task.Delay(250, stoppingToken);
        }
    }

    private async Task SendTimelineAsync(ListeningRoom room, CancellationToken ct)
    {
        var msg = new MonoMessage
        {
            Type = MessageTypes.Timeline,
            RoomId = room.Id,
            Playing = room.Playing,
            MediaOriginUnixMs = room.MediaOriginUnixMs,
            MediaTimeAtOriginMs = room.MediaTimeAtOriginMs,
            MediaTimeMs = room.CurrentMediaTimeMs(),
            DurationMs = _rooms.EffectiveDurationOf(room),
            Epoch = room.ResyncEpoch,
            SourceMode = room.SourceMode
        };
        var bytes = LineFraming.Encode(msg);
        foreach (var id in RoomManager.AllPeers(room))
        {
            if (_connections.Outputs.TryGetValue(id, out var channel))
            {
                await channel.SendAsync(bytes, ct);
            }

            if (_connections.Controls.TryGetValue(id, out var send))
            {
                try
                {
                    await send(msg);
                }
                catch (Exception)
                {
                    _connections.Controls.TryRemove(id, out _);
                }
            }
        }
    }
}
