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
    private readonly CatalogStore _catalog;
    private readonly ILogger<TransportService> _log;
    private DateTimeOffset _lastTimeline = DateTimeOffset.MinValue;
    /// <summary>길이를 물어본 트랙. 열리지 않는 파일에 매 틱마다 다시 묻지 않는다.</summary>
    private readonly HashSet<string> _probed = [];

    public TransportService(
        RoomManager rooms,
        RoomBroadcaster broadcaster,
        ConnectionRegistry connections,
        CatalogStore catalog,
        ILogger<TransportService> log)
    {
        _rooms = rooms;
        _broadcaster = broadcaster;
        _connections = connections;
        _catalog = catalog;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var room in FillMissingDurations())
                {
                    await _broadcaster.PublishAsync(room, stoppingToken);
                }

                foreach (var room in _rooms.AdvanceFinished())
                {
                    await _broadcaster.PublishAsync(room, stoppingToken);
                }

                foreach (var room in _rooms.ProposeAutoplayCandidates())
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

    /// <summary>
    /// 재생 중인 트랙의 길이가 비어 있으면 디코더에게 물어 채운다.
    ///
    /// 태그에 길이가 없는 파일은 스캔에서 0 으로 들어온다. 0 이면 진행 바가 눈금을
    /// 못 잡고, 예전처럼 추정치(3분)를 물리면 그 지점에서 바가 끝에 붙어 멈춘다.
    /// 파일을 여는 일이므로 룸 잠금 밖에서, 트랙당 한 번만 한다.
    /// </summary>
    private List<ListeningRoom> FillMissingDurations()
    {
        var changed = new List<ListeningRoom>();
        foreach (var room in _rooms.List().Where(r => r.Playing))
        {
            var track = room.CurrentTrack(_catalog.Tracks);
            if (track is null || track.DurationMs > 0 || !_probed.Add(track.Id))
            {
                continue;
            }

            try
            {
                using var slicer = AudioSourceFactory.Open(track);
                if (slicer.DurationMs <= 0)
                {
                    continue;
                }

                _catalog.SetTrackDuration(track.Id, slicer.DurationMs);
                changed.Add(room);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "duration probe {Track}", track.Id);
            }
        }

        return changed;
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
