using System.Collections.Concurrent;
using System.Net.Sockets;
using Auralis.Protocol;
using Auralis.Shared;

namespace Auralis.Core;

/// <summary>AATP 소켓 한 개. 컨트롤 프레임과 오디오 프레임이 섞이지 않도록 쓰기를 직렬화한다.</summary>
public sealed class OutputChannel
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public required NetworkStream Stream { get; init; }

    public async Task<bool> SendAsync(byte[] bytes, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(2000, ct))
        {
            return false;
        }

        try
        {
            await Stream.WriteAsync(bytes, ct);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class ConnectionRegistry
{
    public ConcurrentDictionary<string, OutputChannel> Outputs { get; } = new();
    public ConcurrentDictionary<string, Func<AuralisMessage, Task>> Controls { get; } = new();
}

/// <summary>룸 상태를 그 룸의 Control/Output 전원에게 보낸다.</summary>
public sealed class RoomBroadcaster
{
    private readonly ConnectionRegistry _connections;
    private readonly RoomManager _rooms;
    private readonly CatalogStore _catalog;

    public RoomBroadcaster(ConnectionRegistry connections, RoomManager rooms, CatalogStore catalog)
    {
        _connections = connections;
        _rooms = rooms;
        _catalog = catalog;
    }

    public AuralisMessage StateMessage(ListeningRoom room)
    {
        var track = room.CurrentTrack(_catalog.Tracks);
        return new AuralisMessage
        {
            Type = MessageTypes.RoomState,
            RoomId = room.Id,
            RoomName = room.Name,
            Mode = room.Mode,
            Playing = room.Playing,
            MediaOriginUnixMs = room.MediaOriginUnixMs,
            MediaTimeAtOriginMs = room.MediaTimeAtOriginMs,
            Epoch = room.ResyncEpoch,
            TrackId = track?.Id,
            SampleRate = track?.SampleRate,
            BitDepth = track?.BitDepth,
            Channels = track?.Channels,
            IsDsd = track?.IsDsd,
            DurationMs = track?.DurationMs,
            LocalPath = room.SourceMode == PlaybackSourceMode.ClockSync ? track?.LocalPath : null,
            SourceMode = room.SourceMode,
            Body = _rooms.SnapshotJson(room)
        };
    }

    public async Task PublishAsync(ListeningRoom room, CancellationToken ct = default)
    {
        var msg = StateMessage(room);
        var bytes = LineFraming.Encode(msg);
        foreach (var id in RoomManager.AllPeers(room))
        {
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

            if (_connections.Outputs.TryGetValue(id, out var channel))
            {
                await channel.SendAsync(bytes, ct);
            }
        }
    }

    public async Task DirectAsync(string peerId, AuralisMessage msg)
    {
        if (_connections.Controls.TryGetValue(peerId, out var send))
        {
            try
            {
                await send(msg);
            }
            catch (Exception)
            {
                _connections.Controls.TryRemove(peerId, out _);
            }
        }

        if (_connections.Outputs.TryGetValue(peerId, out var channel))
        {
            await channel.SendAsync(LineFraming.Encode(msg));
        }
    }

    /// <summary>카탈로그가 바뀌었을 때(스캔·스트리밍 연동) 모든 Control에 새 목록을 밀어준다.</summary>
    public async Task PushCatalogAsync(AuralisMessage catalogMessage)
    {
        foreach (var (peer, send) in _connections.Controls)
        {
            try
            {
                await send(catalogMessage);
            }
            catch (Exception)
            {
                _connections.Controls.TryRemove(peer, out _);
            }
        }
    }
}
