using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Auralis.Protocol;
using Auralis.Shared;

namespace Auralis.Core;

/// <summary>
/// Core의 두 리스너: Control 컨트롤 플레인(7700)과 AATP 엔드포인트(7701).
/// 루프백이 아닌 Control 접속은 페어링 토큰을 요구한다.
/// </summary>
public sealed class CoreHostedService : BackgroundService
{
    private readonly ILogger<CoreHostedService> _log;
    private readonly CommandProcessor _commands;
    private readonly RoomManager _rooms;
    private readonly RoomBroadcaster _broadcaster;
    private readonly ConnectionRegistry _connections;
    private readonly PairingService _pairing;
    private readonly EndpointRegistry _endpoints;

    public const int ControlPort = 7700;
    public const int AatpPort = 7701;

    public CoreHostedService(
        ILogger<CoreHostedService> log,
        CommandProcessor commands,
        RoomManager rooms,
        RoomBroadcaster broadcaster,
        ConnectionRegistry connections,
        PairingService pairing,
        EndpointRegistry endpoints)
    {
        _log = log;
        _commands = commands;
        _rooms = rooms;
        _broadcaster = broadcaster;
        _connections = connections;
        _pairing = pairing;
        _endpoints = endpoints;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var control = Listen(ControlPort, HandleControlAsync, stoppingToken);
        var aatp = Listen(AatpPort, HandleOutputAsync, stoppingToken);
        _log.LogInformation("Auralis Core · Control TCP :{C} · AATP :{A} · Control UI http://127.0.0.1:7702", ControlPort, AatpPort);
        await Task.WhenAll(control, aatp);
    }

    private async Task Listen(int port, Func<TcpClient, CancellationToken, Task> handler, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => handler(client, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private static bool IsLoopback(TcpClient client)
        => client.Client.RemoteEndPoint is IPEndPoint ep && IPAddress.IsLoopback(ep.Address);

    private async Task HandleControlAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var local = IsLoopback(client);
            string? peerId = null;
            string? peerName = null;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    AuralisMessage? msg;
                    try
                    {
                        msg = JsonSerializer.Deserialize<AuralisMessage>(line, LineFraming.JsonOptions);
                    }
                    catch (JsonException)
                    {
                        await stream.WriteAsync(LineFraming.Encode(new AuralisMessage { Type = MessageTypes.Error, Error = "bad json" }), ct);
                        continue;
                    }

                    if (msg is null) continue;

                    if (msg.Type == MessageTypes.Hello)
                    {
                        // 원격 Control은 페어링 코드를 교환해 받은 세션 토큰이 있어야 한다.
                        if (!local && !_pairing.IsAuthorized(msg.Token))
                        {
                            await stream.WriteAsync(LineFraming.Encode(new AuralisMessage
                            {
                                Type = MessageTypes.Error,
                                Ok = false,
                                Error = "pairing required — Core에서 발급한 코드를 redeem 하세요"
                            }), ct);
                            break;
                        }

                        peerId = string.IsNullOrWhiteSpace(msg.PeerId) ? Guid.NewGuid().ToString("n")[..10] : msg.PeerId;
                        peerName = string.IsNullOrWhiteSpace(msg.DisplayName) ? peerId : msg.DisplayName;
                        var id = peerId;
                        _connections.Controls[id] = m => stream.WriteAsync(LineFraming.Encode(m), ct).AsTask();
                        await stream.WriteAsync(LineFraming.Encode(new AuralisMessage
                        {
                            Type = MessageTypes.Welcome,
                            PeerId = id,
                            Ok = true,
                            Body = $"Auralis Core · catalog ready"
                        }), ct);
                        await stream.WriteAsync(LineFraming.Encode(_commands.CatalogMessage()), ct);
                        continue;
                    }

                    if (msg.Type == MessageTypes.Redeem)
                    {
                        var result = _commands.Execute("anon", msg, null);
                        if (result.Direct is not null)
                        {
                            await stream.WriteAsync(LineFraming.Encode(result.Direct), ct);
                        }

                        continue;
                    }

                    if (peerId is null)
                    {
                        await stream.WriteAsync(LineFraming.Encode(new AuralisMessage { Type = MessageTypes.Error, Error = "hello first" }), ct);
                        continue;
                    }

                    // hello에서 받은 이름을 이후 명령에도 붙인다(멤버 목록·아카이브 참가자).
                    var executed = _commands.Execute(peerId, msg, msg.DisplayName ?? peerName);
                    if (executed.Direct is not null)
                    {
                        await stream.WriteAsync(LineFraming.Encode(executed.Direct), ct);
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
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                _log.LogDebug(ex, "Control closed");
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

    private async Task HandleOutputAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var channel = new OutputChannel { Stream = stream };
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
            string? peerId = null;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    AuralisMessage? msg;
                    try
                    {
                        msg = JsonSerializer.Deserialize<AuralisMessage>(line, LineFraming.JsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (msg is null) continue;

                    switch (msg.Type)
                    {
                        case MessageTypes.OutputHello:
                        {
                            peerId = string.IsNullOrWhiteSpace(msg.PeerId) ? Guid.NewGuid().ToString("n")[..10] : msg.PeerId;
                            _connections.Outputs[peerId] = channel;
                            var cap = new OutputCapability
                            {
                                PeerId = peerId,
                                DisplayName = msg.DisplayName ?? peerId,
                                MaxSampleRate = msg.MaxSampleRate ?? 192000,
                                MaxBitDepth = msg.MaxBitDepth ?? 32,
                                SupportsDsd = msg.SupportsDsd ?? false,
                                ExclusiveMode = msg.ExclusiveMode ?? true,
                                ReportedLatencyMs = msg.LatencyMs ?? 5,
                                HardwareVolume = msg.HardwareVolume ?? true,
                                VolumePercent = msg.Volume ?? 100,
                                Device = msg.Device
                            };
                            _endpoints.Announce(cap, msg.RoomId);

                            if (string.IsNullOrWhiteSpace(msg.RoomId))
                            {
                                await channel.SendAsync(LineFraming.Encode(new AuralisMessage
                                {
                                    Type = MessageTypes.Welcome,
                                    PeerId = peerId,
                                    Ok = true,
                                    Body = "엔드포인트로 등록됨 — 룸에 붙으려면 --room=<id>"
                                }), ct);
                                break;
                            }

                            var joined = _rooms.Join(msg.RoomId, peerId, PeerRole.Output, msg.InviteCode, msg.DisplayName);
                            if (joined.Room is null)
                            {
                                await channel.SendAsync(LineFraming.Encode(new AuralisMessage
                                {
                                    Type = MessageTypes.Error,
                                    Ok = false,
                                    Error = joined.Error
                                }), ct);
                                break;
                            }

                            var registered = _rooms.RegisterOutput(msg.RoomId, cap);
                            await channel.SendAsync(LineFraming.Encode(new AuralisMessage
                            {
                                Type = MessageTypes.Welcome,
                                PeerId = peerId,
                                RoomId = msg.RoomId,
                                Ok = true,
                                Error = registered.Error,
                                SourceMode = registered.Room?.SourceMode,
                                // 팬아웃 페이로드 복호화 키. 데이터 플레인 전용.
                                Token = Convert.ToBase64String(registered.Room?.FanOutKey ?? [])
                            }), ct);
                            if (registered.Room is not null)
                            {
                                await _broadcaster.PublishAsync(registered.Room, ct);
                            }

                            break;
                        }

                        case MessageTypes.ClockPing:
                        {
                            var t2 = ClockSync.UnixMs();
                            var t3 = ClockSync.UnixMs();
                            await channel.SendAsync(LineFraming.Encode(new AuralisMessage
                            {
                                Type = MessageTypes.ClockPong,
                                T1 = msg.T1,
                                T2 = t2,
                                T3 = t3
                            }), ct);
                            break;
                        }

                        case MessageTypes.ClockReport when peerId is not null && msg.RoomId is not null:
                        {
                            var room = _rooms.ReportClock(
                                msg.RoomId,
                                peerId,
                                msg.OffsetMs ?? 0,
                                msg.JitterMs ?? 0,
                                msg.RttMs ?? 0,
                                msg.BufferMs ?? 0,
                                msg.Resyncs ?? 0,
                                msg.Locked ?? false);
                            if (room is not null)
                            {
                                await _broadcaster.PublishAsync(room, ct);
                            }

                            break;
                        }

                        case MessageTypes.Caps when peerId is not null && msg.RoomId is not null:
                        {
                            var cap = new OutputCapability
                            {
                                PeerId = peerId,
                                DisplayName = msg.DisplayName ?? peerId,
                                MaxSampleRate = msg.MaxSampleRate ?? 192000,
                                MaxBitDepth = msg.MaxBitDepth ?? 32,
                                SupportsDsd = msg.SupportsDsd ?? false,
                                ExclusiveMode = msg.ExclusiveMode ?? true,
                                ReportedLatencyMs = msg.LatencyMs ?? 5,
                                HardwareVolume = msg.HardwareVolume ?? true,
                                VolumePercent = msg.Volume ?? 100,
                                Device = msg.Device
                            };
                            var updated = _rooms.RegisterOutput(msg.RoomId, cap);
                            if (updated.Room is not null)
                            {
                                await _broadcaster.PublishAsync(updated.Room, ct);
                            }

                            break;
                        }

                        case MessageTypes.Volume when peerId is not null && msg.RoomId is not null:
                        {
                            var volumeResult = _rooms.SetVolume(msg.RoomId, peerId, peerId, msg.Volume ?? 100);
                            if (volumeResult.Room is not null)
                            {
                                await _broadcaster.PublishAsync(volumeResult.Room, ct);
                            }

                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                _log.LogDebug(ex, "Output closed");
            }
            finally
            {
                if (peerId is not null)
                {
                    _connections.Outputs.TryRemove(peerId, out _);
                    var room = _rooms.DetachOutput(peerId);
                    if (room is not null)
                    {
                        await _broadcaster.PublishAsync(room, CancellationToken.None);
                    }
                }
            }
        }
    }
}
