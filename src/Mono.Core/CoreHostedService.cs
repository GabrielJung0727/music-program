using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// Core의 두 리스너: Control 컨트롤 플레인(7700)과 MATP 엔드포인트(7701).
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
    private readonly TcpExposure _exposure;

    public const int ControlPort = 7700;
    public const int MatpPort = 7701;

    /// <summary>컨트롤 한 줄의 상한. WebSocket 쪽 프레임 상한과 같다.</summary>
    private const int MaxControlLineBytes = 4 * 1024 * 1024;

    /// <summary>출력 워커가 Core 로 보내는 것은 hello · caps · 시계 보고 같은 짧은 줄뿐이다.</summary>
    private const int MaxOutputLineBytes = 256 * 1024;

    public CoreHostedService(
        ILogger<CoreHostedService> log,
        CommandProcessor commands,
        RoomManager rooms,
        RoomBroadcaster broadcaster,
        ConnectionRegistry connections,
        PairingService pairing,
        EndpointRegistry endpoints,
        TcpExposure exposure)
    {
        _log = log;
        _commands = commands;
        _rooms = rooms;
        _broadcaster = broadcaster;
        _connections = connections;
        _pairing = pairing;
        _endpoints = endpoints;
        _exposure = exposure;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var control = Listen(ControlPort, HandleControlAsync, stoppingToken);
        var matp = Listen(MatpPort, HandleOutputAsync, stoppingToken);
        _log.LogInformation("Mono Core · Control TCP {Bind}:{C} · MATP {Bind}:{A} · Control UI http://127.0.0.1:7702{Lan}",
            _exposure.BindAddress, ControlPort, _exposure.BindAddress, MatpPort,
            _exposure.AllowRemote ? " · 다른 PC 의 출력 허용" : "");
        await Task.WhenAll(control, matp);
    }

    private async Task Listen(int port, Func<TcpClient, CancellationToken, Task> handler, CancellationToken ct)
    {
        // 기본은 루프백. 다른 PC 의 출력 워커를 쓰는 사람만 설정으로 LAN 에 연다(NetworkExposure).
        var listener = new TcpListener(_exposure.BindAddress, port);
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
            var reader = new BoundedLineReader(stream, MaxControlLineBytes);
            // 이 연결의 응답과 다른 스레드의 방송이 같은 소켓에 겹쳐 쓰이면 줄이 섞인다.
            // 한 번에 하나씩만 쓴다(WebSocket 쪽에는 원래 이 문이 있었다).
            using var writeGate = new SemaphoreSlim(1, 1);
            var session = new ControlSession(_commands, _rooms, _broadcaster, _connections, _pairing);
            try
            {
                await session.RunAsync(
                    token => reader.ReadLineAsync(token).AsTask(),
                    async (msg, token) =>
                    {
                        var bytes = LineFraming.Encode(msg);
                        await writeGate.WaitAsync(token);
                        try
                        {
                            await stream.WriteAsync(bytes, token);
                        }
                        finally
                        {
                            writeGate.Release();
                        }
                    },
                    IsLoopback(client),
                    ct);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                _log.LogDebug(ex, "Control closed");
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
            var reader = new BoundedLineReader(stream, MaxOutputLineBytes);
            string? peerId = null;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    MonoMessage? msg;
                    try
                    {
                        msg = JsonSerializer.Deserialize<MonoMessage>(line, LineFraming.JsonOptions);
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
                                SampleRates = msg.SampleRates ?? [],
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
                                await channel.SendAsync(LineFraming.Encode(new MonoMessage
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
                                await channel.SendAsync(LineFraming.Encode(new MonoMessage
                                {
                                    Type = MessageTypes.Error,
                                    Ok = false,
                                    Error = joined.Error
                                }), ct);
                                break;
                            }

                            var registered = _rooms.RegisterOutput(msg.RoomId, cap);
                            await channel.SendAsync(LineFraming.Encode(new MonoMessage
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
                            await channel.SendAsync(LineFraming.Encode(new MonoMessage
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
                                msg.Locked ?? false,
                                msg.DeviceState ?? 0,
                                msg.DeviceError);
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
                                SampleRates = msg.SampleRates ?? [],
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
