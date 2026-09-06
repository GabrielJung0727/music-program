using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Mono.Output;
using Mono.Protocol;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// Mono Output — 클럭 슬레이브 오디오 엔드포인트.
// Core가 준 PTS와 클럭 오프셋으로 지정 시각에 출력한다. UI도, 선곡도 하지 않는다.

var host = Arg("--host=") ?? "127.0.0.1";
var roomId = Arg("--room=");
var invite = Arg("--invite=");
var deviceHint = Arg("--device=");
var peerId = Arg("--peer=") ?? "out-" + Guid.NewGuid().ToString("n")[..6];
var displayName = Arg("--name=") ?? Environment.MachineName;
// 장치는 등록 시 한 모드로 고정된다. 묵시적 강등은 없다.
var deviceMode = args.Contains("--shared") ? DeviceMode.SystemShared : DeviceMode.BitPerfectExclusive;
var claimDsd = args.Contains("--dsd");
var preferAsio = args.Contains("--asio");
var startVolume = int.TryParse(Arg("--volume="), out var v0) ? Math.Clamp(v0, 0, 100) : 100;
const int matpPort = 7701;

IAudioOutputDevice renderer = preferAsio
    ? (IAudioOutputDevice?)AsioAudioRenderer.TryCreate(deviceHint) ?? new WasapiOutputDevice(deviceHint, deviceMode)
    : new WasapiOutputDevice(deviceHint, deviceMode);
var caps = renderer.GetSupportedFormats();
var timeline = new Timeline();
var clock = new ClockState();
var frames = new ConcurrentQueue<MatpAudio>();
byte[]? fanOutKey = null;
var volumePercent = startVolume;
var resyncs = 0;
var drops = 0;
var lastLog = DateTimeOffset.MinValue;

Console.WriteLine($"Mono Output {peerId} → {host}:{matpPort}  device={renderer.DeviceName}");
Console.WriteLine(renderer.HardwareVolume ? "하드웨어 볼륨 사용 가능 (bit-perfect 유지)" : "하드웨어 볼륨 없음 — 룸이 허용할 때만 디지털 감쇠");
Console.WriteLine($"mode={deviceMode} exclusiveCapable={caps.SupportsExclusive} " +
                  $"rates=[{string.Join(",", caps.SampleRates)}] depths=[{string.Join(",", caps.BitDepths)}]");

using var client = new TcpClient { NoDelay = true };
await client.ConnectAsync(host, matpPort);
var stream = client.GetStream();
var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 16384, leaveOpen: true);
var writeGate = new SemaphoreSlim(1, 1);
using var cts = new CancellationTokenSource();

await Send(new MonoMessage
{
    Type = MessageTypes.OutputHello,
    PeerId = peerId,
    RoomId = roomId,
    InviteCode = invite,
    Role = "output",
    DisplayName = displayName,
    Device = renderer.DeviceName,
    MaxSampleRate = caps.SampleRates.Count > 0 ? caps.SampleRates.Max() : 48000,
    MaxBitDepth = caps.BitDepths.Count > 0 ? caps.BitDepths.Max() : 16,
    SupportsDsd = claimDsd,
    ExclusiveMode = deviceMode == DeviceMode.BitPerfectExclusive,
    LatencyMs = renderer.LatencyMs,
    HardwareVolume = renderer.HardwareVolume,
    Volume = volumePercent
});

var clockLoop = Task.Run(() => ClockLoopAsync(cts.Token));

// 렌더 루프는 전용 스레드에서 돈다. 스레드풀에서 await 로 돌면 스레드가 계속 바뀌어
// MMCSS 등록(스레드 단위)이 아무 효과가 없다.
var renderThread = new Thread(() =>
{
    var mmcss = Mmcss.Register(out var mmcssStatus);
    var fineTimer = Mmcss.RaiseTimerResolution();
    Console.WriteLine($"render thread · {mmcssStatus} · timer={(fineTimer ? "1ms" : "기본")}");
    try
    {
        RenderLoop(cts.Token);
    }
    catch (OperationCanceledException)
    {
        // 정상 종료
    }
    finally
    {
        Mmcss.Revert(mmcss);
        if (fineTimer) Mmcss.RestoreTimerResolution();
    }
})
{
    IsBackground = true,
    Priority = ThreadPriority.Highest,
    Name = "mono-render"
};
renderThread.Start();

try
{
    while (await reader.ReadLineAsync(cts.Token) is { } line)
    {
        MonoMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<MonoMessage>(line, LineFraming.JsonOptions);
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

            case MessageTypes.MatpAudio when msg.Body is not null:
            {
                var raw = Convert.FromBase64String(msg.Body);
                if (!MatpFrame.TryDecode(raw, fanOutKey, out var audio))
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

await Task.WhenAny(clockLoop, Task.Delay(500));
renderThread.Join(500);
return;

async Task ClockLoopAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested && client.Connected)
    {
        try
        {
            await Send(new MonoMessage { Type = MessageTypes.ClockPing, T1 = ClockSync.UnixMs() });
            if (roomId is not null && clock.Samples > 2)
            {
                await Send(new MonoMessage
                {
                    Type = MessageTypes.ClockReport,
                    RoomId = roomId,
                    PeerId = peerId,
                    OffsetMs = clock.OffsetMs,
                    JitterMs = clock.JitterMs,
                    RttMs = clock.RttMs,
                    BufferMs = (int)renderer.BufferedMs,
                    Resyncs = resyncs + drops,
                    Locked = renderer.Playing && Math.Abs(renderer.BufferedMs - clock.TargetBufferMs) < 40,
                    DeviceState = (int)renderer.GetCurrentState(),
                    DeviceError = renderer.LastError
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

double DepthCeilingMs(MatpAudio frame)
    => RenderGate.DepthCeilingMs(
        clock.TargetBufferMs,
        renderer.LatencyMs,
        RenderGate.FrameDurationMs(frame.Payload.Length, frame.SampleRate, frame.BitDepth, frame.Channels, frame.IsDsd));

// 렌더 루프: PTS가 도래한 프레임만 DAC 버퍼로 넘긴다. 소셜/네트워크 처리와 스레드를 나눈다.
void RenderLoop(CancellationToken ct)
{
    ILocalChunkSource? local = null;
    var lastEpoch = long.MinValue;
    while (!ct.IsCancellationRequested)
    {
        try
        {
            if (timeline.Epoch != lastEpoch)
            {
                var firstLock = lastEpoch == long.MinValue;
                lastEpoch = timeline.Epoch;

                // 지난 epoch 의 프레임만 걷어낸다. 통째로 비우면 곡을 넘긴 직후 Core 가 한꺼번에
                // 보내 둔 새 곡의 룩어헤드(약 320ms)까지 함께 지워져, 새 곡이 앞부분을 잃고
                // 한참 뒤에서 튀어나온다. 수신 루프는 이미 새 epoch 기준으로 받아들이고 있다.
                foreach (var keep in RenderGate.DropStaleEpochs(frames, timeline.Epoch))
                {
                    frames.Enqueue(keep);
                }

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
                            renderer.PushSamples(
                                new AudioBuffer(chunk, 0, chunk.Length, fmt.rate, fmt.depth, fmt.channels, false),
                                volumePercent);
                        }
                    }
                    else if (DateTimeOffset.UtcNow - lastLog > TimeSpan.FromSeconds(10))
                    {
                        lastLog = DateTimeOffset.UtcNow;
                        Console.WriteLine("clock-sync: 로컬 파일이 없습니다 — 각자의 스트리밍 앱에서 같은 트랙을 여세요 (타임라인만 동기화).");
                    }
                }

                Thread.Sleep(20);
                continue;
            }

            local?.Dispose();
            local = null;

            // 프라임 깊이는 스케줄러가 앞당겨 잡아 둔 지터 버퍼와 같아야 동기가 맞는다.
            renderer.PrimeMs = clock.TargetBufferMs;

            while (frames.TryPeek(out var head))
            {
                var playAtLocal = timeline.PlayAtLocalUnixMs(head.PtsMs, clock.OffsetMs, clock.TargetBufferMs);
                var wait = playAtLocal - ClockSync.UnixMs();
                if (wait > 3)
                {
                    break;
                }

                // 버퍼가 깊다고 프레임을 버리면 그 자리에 파형 불연속이 남아 딸깍/지직 소리가 난다.
                // 큐에 그대로 두고 다음 루프에서 다시 시도한다(백프레셔). PTS 게이트가 이미 속도를 맞추므로
                // 버퍼는 곧 빠지고 프레임은 온전히 들어간다.
                if (renderer.Playing && RenderGate.ShouldHold(renderer.BufferedMs, DepthCeilingMs(head)))
                {
                    break;
                }

                frames.TryDequeue(out var frame);
                if (wait < -RenderGate.LateToleranceMs)
                {
                    // 너무 늦게 도착 — 버리고 락을 유지한다. 깊이 제어로 버리는 일은 이제 없으므로
                    // drop 카운터는 오직 이 경우(지각 도착)만 센다.
                    drops++;
                    continue;
                }

                if (frame.IsDsd)
                {
                    if (!claimDsd)
                    {
                        continue;
                    }

                    var (dop, rate, depth, ch) = DopEncoder.Encode(frame.Payload, frame.SampleRate, frame.Channels);
                    if (dop.Length == 0) continue;
                    renderer.PushSamples(new AudioBuffer(dop, 0, dop.Length, rate, depth, ch, false), volumePercent);
                    continue;
                }

                // 스톨 등으로 백프레셔가 감당 못 할 만큼 벌어졌을 때만 비운다. 정상 재생에서는 닿지 않는다.
                if (renderer.Playing && RenderGate.ShouldFlush(renderer.BufferedMs, DepthCeilingMs(frame)))
                {
                    renderer.Flush();
                    resyncs++;
                    continue;
                }

                renderer.PushSamples(
                    new AudioBuffer(frame.Payload, 0, frame.Payload.Length,
                        frame.SampleRate, frame.BitDepth, frame.Channels, frame.IsDsd),
                    volumePercent);
            }

            if (DateTimeOffset.UtcNow - lastLog > TimeSpan.FromSeconds(5) && clock.Samples > 0)
            {
                lastLog = DateTimeOffset.UtcNow;
                Console.WriteLine(
                    $"clock offset={clock.OffsetMs:F2}ms jitter={clock.JitterMs:F2}ms rtt={clock.RttMs:F2}ms " +
                    $"target={clock.TargetBufferMs}ms depth={renderer.BufferedMs:F0}ms resync={resyncs} drop={drops} " +
                    $"state={renderer.GetCurrentState()}" +
                    (renderer.LastError is { } err ? $" · {err}" : ""));
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

        // 1ms 타이머 해상도를 올려 뒀으므로 이 대기는 실제로 ~1ms 다.
        Thread.Sleep(1);
    }

    local?.Dispose();
}

async Task Send(MonoMessage message)
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
