using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Auralis.Output;
using Auralis.Protocol;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// Auralis Output — 클럭 슬레이브 오디오 엔드포인트.
// Core가 준 PTS와 클럭 오프셋으로 지정 시각에 출력한다. UI도, 선곡도 하지 않는다.

var host = Arg("--host=") ?? "127.0.0.1";
var roomId = Arg("--room=");
var invite = Arg("--invite=");
var deviceHint = Arg("--device=");
var peerId = Arg("--peer=") ?? "out-" + Guid.NewGuid().ToString("n")[..6];
var displayName = Arg("--name=") ?? Environment.MachineName;
var forceShared = args.Contains("--shared");
var claimDsd = args.Contains("--dsd");
var startVolume = int.TryParse(Arg("--volume="), out var v0) ? Math.Clamp(v0, 0, 100) : 100;
const int aatpPort = 7701;

var renderer = new Renderer(deviceHint, forceShared);
var timeline = new Timeline();
var clock = new ClockState();
var frames = new ConcurrentQueue<AatpAudio>();
byte[]? fanOutKey = null;
var volumePercent = startVolume;
var resyncs = 0;
var drops = 0;
var lastLog = DateTimeOffset.MinValue;

Console.WriteLine($"Auralis Output {peerId} → {host}:{aatpPort}  device={renderer.DeviceName}");
Console.WriteLine(renderer.HardwareVolume ? "하드웨어 볼륨 사용 가능 (bit-perfect 유지)" : "하드웨어 볼륨 없음 — 룸이 허용할 때만 디지털 감쇠");

using var client = new TcpClient { NoDelay = true };
await client.ConnectAsync(host, aatpPort);
var stream = client.GetStream();
var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 16384, leaveOpen: true);
var writeGate = new SemaphoreSlim(1, 1);
using var cts = new CancellationTokenSource();

await Send(new AuralisMessage
{
    Type = MessageTypes.OutputHello,
    PeerId = peerId,
    RoomId = roomId,
    InviteCode = invite,
    Role = "output",
    DisplayName = displayName,
    Device = renderer.DeviceName,
    MaxSampleRate = renderer.MaxSampleRate,
    MaxBitDepth = renderer.MaxBitDepth,
    SupportsDsd = claimDsd,
    ExclusiveMode = !forceShared,
    LatencyMs = renderer.LatencyMs,
    HardwareVolume = renderer.HardwareVolume,
    Volume = volumePercent
});

var clockLoop = Task.Run(() => ClockLoopAsync(cts.Token));
var renderLoop = Task.Run(() => RenderLoopAsync(cts.Token));

try
{
    while (await reader.ReadLineAsync(cts.Token) is { } line)
    {
        AuralisMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<AuralisMessage>(line, LineFraming.JsonOptions);
        }
        catch (JsonException)
        {
            continue;
        }

        if (msg is null)
        {
            continue;
        }

        switch (msg.Type)
        {
            case MessageTypes.ClockPong when msg.T1 is { } t1 && msg.T2 is { } t2 && msg.T3 is { } t3:
                clock.Update(t1, t2, t3, ClockSync.UnixMs());
                break;

            case MessageTypes.Welcome:
                if (!string.IsNullOrWhiteSpace(msg.Token))
                {
                    var key = Convert.FromBase64String(msg.Token);
                    fanOutKey = key.Length == 32 ? key : null;
                }

                if (!string.IsNullOrWhiteSpace(msg.RoomId))
                {
                    roomId = msg.RoomId;
                }

                Console.WriteLine($"welcome room={roomId ?? "-"} {(msg.Error is null ? "" : "· " + msg.Error)}");
                break;

            case MessageTypes.RoomState:
                timeline.Apply(msg);
                var newVolume = VolumeFromSnapshot(msg.Body, peerId);
                if (newVolume is { } vol && vol != volumePercent)
                {
                    volumePercent = vol;
                    renderer.SetHardwareVolume(vol);
                    Console.WriteLine($"volume → {vol}%");
                }

                Console.WriteLine($"timeline playing={msg.Playing} track={msg.TrackId} {msg.BitDepth}/{msg.SampleRate} source={msg.SourceMode} epoch={msg.Epoch}");
                break;

            case MessageTypes.Timeline:
                timeline.Apply(msg);
                break;

            case MessageTypes.AatpAudio when msg.Body is not null:
            {
                var raw = Convert.FromBase64String(msg.Body);
                if (!AatpFrame.TryDecode(raw, fanOutKey, out var audio))
                {
                    continue;
                }

                if (audio.Epoch != timeline.Epoch)
                {
                    continue;
                }

                if (audio.IsDsd && !claimDsd)
                {
                    continue;
                }

                frames.Enqueue(audio);
                break;
            }

            case MessageTypes.Error:
                Console.WriteLine("error: " + msg.Error);
                break;
        }
    }
}
catch (OperationCanceledException) { }
finally
{
    cts.Cancel();
    renderer.Dispose();
}

await Task.WhenAny(Task.WhenAll(clockLoop, renderLoop), Task.Delay(500));
return;

async Task ClockLoopAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested && client.Connected)
    {
        try
        {
            await Send(new AuralisMessage { Type = MessageTypes.ClockPing, T1 = ClockSync.UnixMs() });
            if (roomId is not null && clock.Samples > 2)
            {
                await Send(new AuralisMessage
                {
                    Type = MessageTypes.ClockReport,
                    RoomId = roomId,
                    PeerId = peerId,
                    OffsetMs = clock.OffsetMs,
                    JitterMs = clock.JitterMs,
                    RttMs = clock.RttMs,
                    BufferMs = (int)renderer.BufferedMs,
                    Resyncs = resyncs + drops,
                    Locked = renderer.Playing && Math.Abs(renderer.BufferedMs - clock.TargetBufferMs) < 40
                });
            }
        }
        catch (Exception)
        {
            return;
        }

        await Task.Delay(1000, ct);
    }
}

// 렌더 루프: PTS가 도래한 프레임만 DAC 버퍼로 넘긴다. 소셜/네트워크 처리와 스레드를 나눈다.
async Task RenderLoopAsync(CancellationToken ct)
{
    LocalFileRenderer? local = null;
    var lastEpoch = long.MinValue;
    while (!ct.IsCancellationRequested)
    {
        try
        {
            if (timeline.Epoch != lastEpoch)
            {
                var firstLock = lastEpoch == long.MinValue;
                lastEpoch = timeline.Epoch;
                frames.Clear();
                renderer.Flush();
                local?.Dispose();
                local = null;
                if (!firstLock)
                {
                    resyncs++;
                }
            }

            if (timeline.ClockSyncMode)
            {
                // Clock-sync: Core는 파일을 보내지 않는다. 같은 트랙을 내 소스로 열어 같은 시각에 재생한다.
                if (timeline.Playing && timeline.LocalPath is not null)
                {
                    local ??= LocalFileRenderer.TryOpen(timeline.LocalPath);
                    if (local is not null)
                    {
                        var mediaNow = timeline.MediaTimeMs(clock.OffsetMs);
                        var chunk = local.Read(mediaNow, 40, out var fmt);
                        if (chunk.Length > 0)
                        {
                            renderer.Push(chunk, fmt.rate, fmt.depth, fmt.channels, volumePercent);
                        }
                    }
                    else if (DateTimeOffset.UtcNow - lastLog > TimeSpan.FromSeconds(10))
                    {
                        lastLog = DateTimeOffset.UtcNow;
                        Console.WriteLine("clock-sync: 로컬 파일이 없습니다 — 각자의 스트리밍 앱에서 같은 트랙을 여세요 (타임라인만 동기화).");
                    }
                }

                await Task.Delay(20, ct);
                continue;
            }

            local?.Dispose();
            local = null;

            while (frames.TryPeek(out var head))
            {
                var playAtLocal = timeline.PlayAtLocalUnixMs(head.PtsMs, clock.OffsetMs, clock.TargetBufferMs);
                var wait = playAtLocal - ClockSync.UnixMs();
                if (wait > 3)
                {
                    break;
                }

                frames.TryDequeue(out var frame);
                if (wait < -clock.TargetBufferMs * 4)
                {
                    // 너무 늦게 도착 — 버리고 락을 유지한다.
                    resyncs++;
                    continue;
                }

                if (frame.IsDsd)
                {
                    continue;
                }

                // 목표 지연 = 지터 버퍼 + DAC 자체 지연. 그보다 깊어지면 락을 유지한 채 지연만 걷어낸다.
                var targetDepth = clock.TargetBufferMs + renderer.LatencyMs;
                if (renderer.Playing && renderer.BufferedMs > targetDepth + 60)
                {
                    // 큰 이탈(스톨 이후)은 한 번에 비우고 다시 락한다.
                    renderer.Flush();
                    resyncs++;
                    continue;
                }

                if (renderer.Playing && renderer.BufferedMs > targetDepth + 10)
                {
                    drops++;
                    continue;
                }

                renderer.Push(frame.Payload, frame.SampleRate, frame.BitDepth, frame.Channels, volumePercent);
            }

            if (DateTimeOffset.UtcNow - lastLog > TimeSpan.FromSeconds(5) && clock.Samples > 0)
            {
                lastLog = DateTimeOffset.UtcNow;
                Console.WriteLine(
                    $"clock offset={clock.OffsetMs:F2}ms jitter={clock.JitterMs:F2}ms rtt={clock.RttMs:F2}ms " +
                    $"target={clock.TargetBufferMs}ms depth={renderer.BufferedMs:F0}ms resync={resyncs} drop={drops} " +
                    $"{(renderer.Exclusive ? "Exclusive" : "Shared")}");
            }
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine("render: " + ex.Message);
        }

        await Task.Delay(3, ct);
    }

    local?.Dispose();
}

async Task Send(AuralisMessage message)
{
    await writeGate.WaitAsync();
    try
    {
        await stream.WriteAsync(LineFraming.Encode(message));
    }
    finally
    {
        writeGate.Release();
    }
}

static int? VolumeFromSnapshot(string? body, string peerId)
{
    if (string.IsNullOrWhiteSpace(body))
    {
        return null;
    }

    try
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("outputs", out var outputs))
        {
            return null;
        }

        foreach (var output in outputs.EnumerateArray())
        {
            if (output.TryGetProperty("peerId", out var id) && id.GetString() == peerId
                && output.TryGetProperty("volumePercent", out var vol))
            {
                return vol.GetInt32();
            }
        }
    }
    catch (JsonException)
    {
        return null;
    }

    return null;
}

string? Arg(string prefix) => args.FirstOrDefault(a => a.StartsWith(prefix))?[prefix.Length..];
