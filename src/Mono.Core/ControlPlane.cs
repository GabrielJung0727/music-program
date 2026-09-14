using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// Control 컨트롤 플레인 한 세션. 전송 계층(TCP 라인 / WebSocket 텍스트 프레임)을
/// 델리게이트로 받아 같은 루프를 돌린다. 웹 UI와 네이티브 Control이 같은 규약을 쓴다.
/// </summary>
public sealed class ControlSession
{
    private readonly CommandProcessor _commands;
    private readonly RoomManager _rooms;
    private readonly RoomBroadcaster _broadcaster;
    private readonly ConnectionRegistry _connections;
    private readonly PairingService _pairing;

    public ControlSession(
        CommandProcessor commands,
        RoomManager rooms,
        RoomBroadcaster broadcaster,
        ConnectionRegistry connections,
        PairingService pairing)
    {
        _commands = commands;
        _rooms = rooms;
        _broadcaster = broadcaster;
        _connections = connections;
        _pairing = pairing;
    }

    /// <param name="readLine">다음 메시지 한 줄. null이면 연결 종료.</param>
    /// <param name="send">한 메시지 전송. 전송 실패는 예외로 던져 루프를 끝낸다.</param>
    /// <param name="isLocal">루프백 접속이면 페어링 토큰을 요구하지 않는다.</param>
    public async Task RunAsync(
        Func<CancellationToken, Task<string?>> readLine,
        Func<MonoMessage, CancellationToken, Task> send,
        bool isLocal,
        CancellationToken ct)
    {
        string? peerId = null;
        string? peerName = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await readLine(ct);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                MonoMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<MonoMessage>(line, LineFraming.JsonOptions);
                }
                catch (JsonException)
                {
                    await send(new MonoMessage { Type = MessageTypes.Error, Error = "bad json" }, ct);
                    continue;
                }

                if (msg is null) continue;

                if (msg.Type == MessageTypes.Hello)
                {
                    if (!isLocal && !_pairing.IsAuthorized(msg.Token))
                    {
                        await send(new MonoMessage
                        {
                            Type = MessageTypes.Error,
                            Ok = false,
                            Error = "pairing required — Core에서 발급한 코드를 redeem 하세요"
                        }, ct);
                        break;
                    }

                    peerId = string.IsNullOrWhiteSpace(msg.PeerId) ? Guid.NewGuid().ToString("n")[..10] : msg.PeerId;
                    peerName = string.IsNullOrWhiteSpace(msg.DisplayName) ? peerId : msg.DisplayName;
                    var id = peerId;
                    _connections.Controls[id] = m => send(m, ct);
                    await send(new MonoMessage
                    {
                        Type = MessageTypes.Welcome,
                        PeerId = id,
                        Ok = true,
                        Body = "Mono Core · catalog ready"
                    }, ct);
                    await send(_commands.CatalogMessage(), ct);
                    continue;
                }

                if (msg.Type == MessageTypes.Redeem)
                {
                    var redeemed = _commands.Execute("anon", msg, null);
                    if (redeemed.Direct is not null)
                    {
                        await send(redeemed.Direct, ct);
                    }

                    continue;
                }

                if (peerId is null)
                {
                    await send(new MonoMessage { Type = MessageTypes.Error, Error = "hello first" }, ct);
                    continue;
                }

                var executed = _commands.Execute(peerId, msg, msg.DisplayName ?? peerName);
                if (executed.Direct is not null)
                {
                    await send(executed.Direct, ct);
                }

                if (executed.BroadcastAll is not null)
                {
                    await _broadcaster.PushCatalogAsync(executed.BroadcastAll);
                }

                if (executed.BroadcastRoom is not null)
                {
                    await _broadcaster.PublishAsync(executed.BroadcastRoom, ct);
                }
            }
        }
        finally
        {
            if (peerId is not null)
            {
                _connections.Controls.TryRemove(peerId, out _);
                foreach (var room in _rooms.RoomsOf(peerId))
                {
                    var left = _rooms.Leave(room.Id, peerId);
                    if (left.Room is not null)
                    {
                        await _broadcaster.PublishAsync(left.Room, CancellationToken.None);
                    }
                }

                _commands.Forget(peerId);
            }
        }
    }
}

/// <summary>
/// 브라우저/WebView2 UI용 컨트롤 플레인. TCP 7700과 동일한 MonoMessage 규약을
/// WebSocket 텍스트 프레임 하나당 한 메시지로 실어 나른다.
/// </summary>
public static class ControlWebSocket
{
    private const int MaxFrameBytes = 4 * 1024 * 1024;

    public static async Task HandleAsync(HttpContext http, ControlSession session, CancellationToken ct)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            await http.Response.WriteAsync("websocket only", ct);
            return;
        }

        using var socket = await http.WebSockets.AcceptWebSocketAsync();
        var isLocal = http.Connection.RemoteIpAddress is null
                      || System.Net.IPAddress.IsLoopback(http.Connection.RemoteIpAddress);

        // 웹소켓은 동시 쓰기를 허용하지 않는다. 브로드캐스트와 응답이 겹치므로 직렬화한다.
        var writeGate = new SemaphoreSlim(1, 1);
        var buffer = new byte[16 * 1024];
        var pending = new MemoryStream();

        async Task<string?> ReadLineAsync(CancellationToken token)
        {
            pending.SetLength(0);
            while (true)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                }
                catch (WebSocketException)
                {
                    return null;
                }

                if (result.MessageType == WebSocketMessageType.Close) return null;
                pending.Write(buffer, 0, result.Count);
                if (pending.Length > MaxFrameBytes) return null;
                if (result.EndOfMessage) break;
            }

            return Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length);
        }

        async Task SendAsync(MonoMessage message, CancellationToken token)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(message, LineFraming.JsonOptions);
            await writeGate.WaitAsync(token);
            try
            {
                if (socket.State != WebSocketState.Open) return;
                await socket.SendAsync(json, WebSocketMessageType.Text, true, token);
            }
            finally
            {
                writeGate.Release();
            }
        }

        try
        {
            await session.RunAsync(ReadLineAsync, SendAsync, isLocal, ct);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
            // 탭을 닫으면 정상적으로 여기로 온다.
        }
        finally
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch (WebSocketException) { }
            }
        }
    }
}
