using System.Collections.Concurrent;
using System.Net.Sockets;
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Core;

/// <summary>MATP 소켓 한 개. 컨트롤 프레임과 오디오 프레임이 섞이지 않도록 쓰기를 직렬화한다.</summary>
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
    /// <summary>이보다 많이 쌓이면 지금 붙어 있지 않은 id 의 비밀값부터 버린다.</summary>
    private const int MaxRememberedPeers = 16384;

    private readonly ConcurrentDictionary<string, string> _secrets = new();

    public ConcurrentDictionary<string, OutputChannel> Outputs { get; } = new();
    public ConcurrentDictionary<string, Func<MonoMessage, Task>> Controls { get; } = new();

    /// <summary>
    /// 접속한 쪽이 쓸 peer id 를 정한다.
    ///
    /// 방의 권한은 전부 peer id 비교다(호스트인가, DJ 인가). 그런데 id 는 접속하는 쪽이 고르고,
    /// 호스트의 id 는 스냅샷에 실려 방의 모두에게 보인다. 그대로 받아 주면 hello 에 호스트의 id 를
    /// 적는 것만으로 방을 넘겨받는다. 그래서 id 를 처음 내줄 때 비밀값을 같이 내주고,
    /// 같은 id 로 다시 붙으려면 그 값을 내게 한다. 값이 없거나 틀리면 새 id 를 준다.
    /// </summary>
    public (string PeerId, string Secret) Claim(string? requestedId, string? secret)
    {
        var id = requestedId?.Trim();
        if (!string.IsNullOrEmpty(id) && id.Length <= 64)
        {
            if (_secrets.TryGetValue(id, out var known))
            {
                if (secret is not null && FixedTimeEquals(known, secret)) return (id, known);
            }
            else
            {
                var first = NewSecret();
                if (_secrets.TryAdd(id, first))
                {
                    Trim();
                    return (id, first);
                }
            }
        }

        while (true)
        {
            var fresh = Guid.NewGuid().ToString("n")[..10];
            var value = NewSecret();
            if (!_secrets.TryAdd(fresh, value)) continue;
            Trim();
            return (fresh, value);
        }
    }

    /// <summary>
    /// 이 연결이 등록한 송신 함수일 때만 지운다. 같은 id 로 새 연결이 이미 붙었다면 그대로 둔다 —
    /// 새로 고침 직후 옛 소켓의 정리 코드가 방금 붙은 연결을 지워 버리던 경합을 막는다.
    /// </summary>
    public bool ReleaseControl(string peerId, Func<MonoMessage, Task> sender)
        => Controls.TryRemove(new KeyValuePair<string, Func<MonoMessage, Task>>(peerId, sender));

    private static string NewSecret()
        => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    private static bool FixedTimeEquals(string a, string b)
        => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    private void Trim()
    {
        if (_secrets.Count <= MaxRememberedPeers) return;
        foreach (var id in _secrets.Keys)
        {
            if (!Controls.ContainsKey(id)) _secrets.TryRemove(id, out _);
        }
    }
}

/// <summary>룸 상태를 그 룸의 Control/Output 전원에게 보낸다.</summary>
public sealed class RoomBroadcaster
{
    private readonly ConnectionRegistry _connections;
    private readonly RoomManager _rooms;
    private readonly CatalogStore _catalog;
    private readonly StreamingHub _streaming;

    public RoomBroadcaster(ConnectionRegistry connections, RoomManager rooms, CatalogStore catalog, StreamingHub streaming)
    {
        _connections = connections;
        _rooms = rooms;
        _catalog = catalog;
        _streaming = streaming;
    }

    public MonoMessage StateMessage(ListeningRoom room)
    {
        var track = room.CurrentTrack(_catalog.Tracks);
        var clockSync = room.SourceMode == PlaybackSourceMode.ClockSync;
        var path = clockSync && track is not null ? PlayablePath(track) : null;

        // 다음 곡을 미리 알려 준다. 출력이 끝나기 전에 열어 두어야 끊김 없이 잇거나 겹칠 수 있다.
        var next = clockSync && track is not null ? _rooms.PeekNextTrack(room) : null;

        return new MonoMessage
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
            LocalPath = path,
            NextTrackId = next?.Id,
            NextLocalPath = next is null ? null : PlayablePath(next),
            Transition = room.Transition,
            CrossfadeMs = room.CrossfadeMs,
            SeamlessEpoch = room.SeamlessEpoch,
            SourceMode = room.SourceMode,
            Body = _rooms.SnapshotJson(room)
        };
    }

    private string? PlayablePath(Track track)
        => track.Source is StreamingProvider.Tidal or StreamingProvider.Qobuz
            ? _streaming.ResolvePlayablePath(track)
            : track.LocalPath;

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
                    _connections.ReleaseControl(id, send);
                }
            }

            if (_connections.Outputs.TryGetValue(id, out var channel))
            {
                await channel.SendAsync(bytes, ct);
            }
        }
    }

    public async Task DirectAsync(string peerId, MonoMessage msg)
    {
        if (_connections.Controls.TryGetValue(peerId, out var send))
        {
            try
            {
                await send(msg);
            }
            catch (Exception)
            {
                _connections.ReleaseControl(peerId, send);
            }
        }

        if (_connections.Outputs.TryGetValue(peerId, out var channel))
        {
            await channel.SendAsync(LineFraming.Encode(msg));
        }
    }

    /// <summary>카탈로그가 바뀌었을 때(스캔·스트리밍 연동) 모든 Control에 새 목록을 밀어준다.</summary>
    public async Task PushCatalogAsync(MonoMessage catalogMessage)
    {
        foreach (var (peer, send) in _connections.Controls)
        {
            try
            {
                await send(catalogMessage);
            }
            catch (Exception)
            {
                _connections.ReleaseControl(peer, send);
            }
        }
    }
}
