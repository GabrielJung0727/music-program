using System.Text.Json;
using Mono.Protocol;
using Mono.Shared;
using NAudio.Wave;

namespace Mono.Output;

/// <summary>
/// Core가 방송한 재생 타임라인의 한 순간. 바뀌지 않는다.
///
/// 수신 스레드가 메시지를 받을 때마다 새로 만들어 통째로 바꿔 끼운다. 렌더 스레드는 한 바퀴에
/// 한 번 이걸 집어 들고 그 바퀴 내내 같은 것을 본다 — 필드를 하나씩 고쳐 쓰면 세대는 새것인데
/// 원점은 옛것인 순간을 렌더 스레드가 읽을 수 있다.
/// </summary>
public sealed record TimelineState(
    bool Playing,
    long Epoch,
    long SeamlessEpoch,
    long OriginUnixMs,
    long TimeAtOriginMs,
    long DurationMs,
    string? TrackId,
    string? LocalPath,
    string? NextTrackId,
    string? NextLocalPath,
    bool ClockSyncMode,
    bool IsDsd,
    RoomMode Mode,
    TrackTransition Transition,
    int CrossfadeMs)
{
    public static TimelineState Empty { get; } = new(
        false, 0, -1, 0, 0, 0, null, null, null, null, false, false,
        RoomMode.OpenLounge, TrackTransition.Gapless, 5000);

    /// <summary>
    /// 이 세대는 곡이 끝까지 재생되어 넘어온 것인가. 그렇다면 재생하던 것을 끊지 않고 이어 간다.
    /// </summary>
    public bool Seamless => SeamlessEpoch == Epoch;

    /// <summary>현재 media_time. offset은 (Core 시계 − 로컬 시계).</summary>
    public long MediaTimeMs(double offsetMs)
    {
        if (!Playing)
        {
            return TimeAtOriginMs;
        }

        var coreNow = ClockSync.UnixMs() + (long)Math.Round(offsetMs);
        return TimeAtOriginMs + (coreNow - OriginUnixMs);
    }

    /// <summary>이 PTS를 로컬 시계 기준 언제 내보내야 하는가.</summary>
    public long PlayAtLocalUnixMs(long ptsMs, double offsetMs, int bufferMs)
        => ClockSync.PlayAtUnixMs(OriginUnixMs, TimeAtOriginMs, ptsMs, bufferMs) - (long)Math.Round(offsetMs);
}

/// <summary>Core가 방송한 재생 타임라인의 로컬 사본.</summary>
public sealed class Timeline
{
    private volatile TimelineState _state = TimelineState.Empty;

    /// <summary>지금의 타임라인. 렌더 루프는 한 바퀴에 한 번만 읽어 그 바퀴 동안 쓴다.</summary>
    public TimelineState State => _state;

    public bool Playing => _state.Playing;
    public long Epoch => _state.Epoch;
    public string? TrackId => _state.TrackId;
    public string? LocalPath => _state.LocalPath;
    public long DurationMs => _state.DurationMs;
    public bool ClockSyncMode => _state.ClockSyncMode;
    public bool IsDsd => _state.IsDsd;

    public void Apply(MonoMessage msg)
    {
        var s = _state;

        // A slow broadcast from before a seek must not rewind the output.
        if (msg.Epoch is { } epoch && epoch < s.Epoch) return;

        s = s with
        {
            Playing = msg.Playing ?? s.Playing,
            OriginUnixMs = msg.MediaOriginUnixMs ?? s.OriginUnixMs,
            TimeAtOriginMs = msg.MediaTimeAtOriginMs ?? s.TimeAtOriginMs,
            Epoch = msg.Epoch ?? s.Epoch,
            DurationMs = msg.DurationMs ?? s.DurationMs
        };

        if (msg.Type == MessageTypes.RoomState)
        {
            s = s with
            {
                TrackId = msg.TrackId,
                LocalPath = msg.LocalPath,
                NextTrackId = msg.NextTrackId,
                NextLocalPath = msg.NextLocalPath,
                IsDsd = msg.IsDsd ?? false,
                Mode = msg.Mode ?? s.Mode,
                Transition = msg.Transition ?? s.Transition,
                CrossfadeMs = msg.CrossfadeMs ?? s.CrossfadeMs,
                SeamlessEpoch = msg.SeamlessEpoch ?? s.SeamlessEpoch
            };
        }

        if (msg.SourceMode is { } mode)
        {
            s = s with { ClockSyncMode = mode == PlaybackSourceMode.ClockSync };
        }

        _state = s;
    }

    /// <summary>현재 media_time. offset은 (Core 시계 − 로컬 시계).</summary>
    public long MediaTimeMs(double offsetMs) => _state.MediaTimeMs(offsetMs);

    /// <summary>이 PTS를 로컬 시계 기준 언제 내보내야 하는가.</summary>
    public long PlayAtLocalUnixMs(long ptsMs, double offsetMs, int bufferMs)
        => _state.PlayAtLocalUnixMs(ptsMs, offsetMs, bufferMs);
}

/// <summary>NTP 스타일 오프셋·지터 추정. 적응형 지터 버퍼 목표를 함께 계산한다.</summary>
public sealed class ClockState
{
    private double _previousOffset;

    public double OffsetMs { get; private set; }
    public double JitterMs { get; private set; }
    public double RttMs { get; private set; } = 10;
    public int Samples { get; private set; }

    public int TargetBufferMs => ClockSync.JitterBufferMs(RttMs, JitterMs);

    public void Update(long t1, long t2, long t3, long t4)
    {
        var sample = ClockSync.OffsetMs(t1, t2, t3, t4);
        var rtt = ClockSync.RttMs(t1, t2, t3, t4);
        RttMs = Samples == 0 ? rtt : ClockSync.Smooth(RttMs, rtt);
        OffsetMs = Samples == 0 ? sample : ClockSync.Smooth(OffsetMs, sample);
        JitterMs = Samples == 0 ? 0 : ClockSync.UpdateJitter(JitterMs, _previousOffset, sample);
        _previousOffset = sample;
        Samples++;
    }
}

/// <summary>
/// 룸 스냅샷(room_state 의 body)에서 출력 워커가 쓰는 값만 꺼낸다 — 내 볼륨과 DSP 설정.
/// </summary>
public static class OutputSnapshot
{
    public static (int? Volume, DspSettings? Dsp) Parse(string? body, string peerId)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            int? volume = null;
            if (root.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Array)
            {
                foreach (var output in outputs.EnumerateArray())
                {
                    if (output.TryGetProperty("peerId", out var id) && id.GetString() == peerId
                        && output.TryGetProperty("volumePercent", out var vol) && vol.TryGetInt32(out var percent))
                    {
                        volume = percent;
                    }
                }
            }

            return (volume, ParseDsp(root));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static DspSettings? ParseDsp(JsonElement root)
    {
        if (!root.TryGetProperty("dspEnabled", out var enabled)
            || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        // 오디오파일 룸은 설정이 무엇이든 가공하지 않는다. Core 의 DspSettings.From 과 같은 규칙이다.
        var mode = (RoomMode)Int(root, "mode", (int)RoomMode.OpenLounge);
        if (mode == RoomMode.Audiophile) return DspSettings.Off;

        var preset = Int(root, "dspPreset", 0);
        return new DspSettings(
            enabled.GetBoolean(),
            Enum.IsDefined(typeof(DspPresetKind), preset) ? (DspPresetKind)preset : DspPresetKind.Off,
            Float(root, "headroomDb"),
            String(root, "easyEqJson"),
            Float(root, "speakerDelayMsLeft"),
            Float(root, "speakerDelayMsRight"),
            Float(root, "speakerGainLeftDb"),
            Float(root, "speakerGainRightDb"),
            String(root, "deviceEqProfile"),
            String(root, "convolutionIrPath"));
    }

    private static int Int(JsonElement root, string name, int fallback)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;

    private static float Float(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : 0f;

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>PCM 스트림의 모양. 이게 같아야 두 곡을 한 장치 스트림에 이어 붙일 수 있다.</summary>
public readonly record struct PcmFormat(int Rate, int Depth, int Channels)
{
    public int BytesPerFrame => Math.Max(1, Channels) * Math.Max(1, Depth / 8);

    public double MsOf(long frames) => Rate <= 0 ? 0 : frames * 1000.0 / Rate;
}

/// <summary>
/// Clock-sync 로컬 소스. 파일 하나를 프레임 단위로 이어 읽는다.
///
/// 소스는 자리만 지킨다. 언제 이어 읽고 언제 옮길지는 위(<see cref="LocalPlayer"/>)가 정한다 —
/// 그 판단이 소스마다 따로 있으면 WAV 와 FLAC 이 같은 타임라인에서 다르게 움직인다.
/// </summary>
public interface ILocalChunkSource : IDisposable
{
    PcmFormat Format { get; }

    /// <summary>전체 길이(프레임). 알 수 없으면 0.</summary>
    long TotalFrames { get; }

    /// <summary>다음에 내줄 프레임. 아직 자리를 잡지 않았으면 -1.</summary>
    long Position { get; }

    void Seek(long frame);

    /// <summary>이어서 읽는다. 끝에 닿으면 요청보다 짧게, 더 없으면 빈 배열을 돌려준다.</summary>
    byte[] ReadNext(int frames);
}

public static class LocalChunkSourceExtensions
{
    /// <summary>
    /// media_time 을 주고 읽는 옛 방식. 이어 재생이면 그대로 이어 읽고, 타임라인에서 벗어났을 때만
    /// 자리를 옮긴다(<see cref="RenderGate.LocalCursorLost"/>).
    /// </summary>
    public static byte[] Read(
        this ILocalChunkSource source, long mediaTimeMs, int durationMs, out (int rate, int depth, int channels) format)
    {
        var f = source.Format;
        format = (f.Rate, f.Depth, f.Channels);
        if (f.Rate <= 0 || durationMs <= 0) return [];

        var requested = Math.Max(0, mediaTimeMs) * f.Rate / 1000;
        if (RenderGate.LocalCursorLost(source.Position, requested, f.Rate))
        {
            source.Seek(requested);
        }

        return source.ReadNext(Math.Max(1, durationMs * f.Rate / 1000));
    }
}

/// <summary>
/// Clock-sync 모드에서 이 엔드포인트가 자기 로컬 파일을 직접 여는 경로.
/// (파일은 네트워크로 오지 않는다 — 라이선스 분기 A)
/// </summary>
public sealed class LocalFileRenderer : ILocalChunkSource
{
    private readonly FileStream _fs;
    private readonly WavHeader _header;
    private readonly long _totalFrames;
    private long _next = -1;

    private LocalFileRenderer(FileStream fs, WavHeader header)
    {
        _fs = fs;
        _header = header;
        _totalFrames = header.DataSize / header.SourceBytesPerFrame;

        // 뎁스는 장치가 여는 규격(16/24)으로 알린다 — 바이트도 ReadNext 에서 같은 규격으로 접는다.
        Format = new PcmFormat(header.SampleRate, header.DeviceBits, header.Channels);
    }

    public PcmFormat Format { get; }
    public long TotalFrames => _totalFrames;
    public long Position => _next;

    public static ILocalChunkSource? TryOpen(string path)
    {
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return MfStreamingLocalRenderer.TryOpen(path);

        if (!File.Exists(path))
            return null;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".flac" or ".mp3" or ".m4a" or ".aac" or ".aiff" or ".aif" or ".wma" or ".mp4" or ".alac")
            return MfStreamingLocalRenderer.TryOpen(path);

        // This reader returns ordinary PCM only. DoP is not decoded PCM and
        // must never be sent through the PCM/float/volume conversion path.
        if (ext is ".dsf" or ".dff")
            return null;

        if (!ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var fs = File.OpenRead(path);
            var header = WavHeader.Parse(fs);
            if (header is null)
            {
                fs.Dispose();
                return null;
            }

            return new LocalFileRenderer(fs, header);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Seek(long frame) => _next = Math.Clamp(frame, 0, _totalFrames);

    public byte[] ReadNext(int frames)
    {
        if (_next < 0) _next = 0;

        var bpf = _header.SourceBytesPerFrame;
        var count = (int)Math.Min(frames, _totalFrames - _next);
        if (count <= 0)
            return [];

        var buffer = new byte[count * bpf];
        _fs.Position = _header.DataPosition + _next * bpf;
        var read = 0;
        while (read < buffer.Length)
        {
            var n = _fs.Read(buffer, read, buffer.Length - read);
            if (n <= 0) break;
            read += n;
        }

        if (read <= 0) return [];
        if (read < buffer.Length) buffer = buffer[..(read / bpf * bpf)];
        _next += buffer.Length / bpf;

        return PcmSamples.ToDeviceDepth(buffer, _header.SourceBits, _header.Encoding, out _);
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>FLAC/MP3 등 Media Foundation 구간 디코드 (Clock-sync 로컬). 전량 RAM 적재 없음.</summary>
file sealed class MfStreamingLocalRenderer : ILocalChunkSource
{
    private readonly WaveStream _reader;
    private readonly ISampleProvider? _samples;
    private readonly object _gate = new();
    private readonly int _blockAlign;

    /// <summary>
    /// 디코더가 내주는 정수 PCM 의 뎁스. 0 이면 정수 PCM 이 아니라서 float 를 거쳐 받는다.
    /// 정수로 받을 수 있으면 float 를 아예 거치지 않는다 — 비트퍼펙트 경로에 계산을 끼울 이유가 없다.
    /// </summary>
    private readonly int _pcmBits;

    private long _next = -1;

    private MfStreamingLocalRenderer(WaveStream reader)
    {
        _reader = reader;
        var wf = reader.WaveFormat;
        _blockAlign = Math.Max(1, wf.BlockAlign);
        _pcmBits = reader is not AudioFileReader
                   && wf.Encoding == WaveFormatEncoding.Pcm
                   && wf.BitsPerSample is 16 or 24 or 32
            ? wf.BitsPerSample
            : 0;
        _samples = _pcmBits != 0 ? null : reader as ISampleProvider ?? reader.ToSampleProvider();

        // 어떤 원본이든 24bit 로 낸다. 16bit 는 하위 바이트를 0 으로 채우므로 값은 그대로다.
        Format = new PcmFormat(wf.SampleRate, 24, Math.Max(wf.Channels, 1));
        TotalFrames = reader.Length > 0 ? reader.Length / _blockAlign : 0;
    }

    public PcmFormat Format { get; }
    public long TotalFrames { get; }
    public long Position => _next;

    public static MfStreamingLocalRenderer? TryOpen(string path)
    {
        try
        {
            WaveStream reader = Path.GetExtension(path).ToLowerInvariant() is ".aiff" or ".aif" or ".wav"
                ? new AudioFileReader(path)
                : new Mono.Audio.PositionedMediaFoundationReader(path);
            return new MfStreamingLocalRenderer(reader);
        }
        catch { return null; }
    }

    public void Seek(long frame)
    {
        lock (_gate)
        {
            var target = Math.Max(0, frame);
            _reader.Position = target * _blockAlign;
            _next = target;
        }
    }

    public byte[] ReadNext(int frames)
    {
        if (frames <= 0) return [];

        lock (_gate)
        {
            if (_next < 0)
            {
                _reader.Position = 0;
                _next = 0;
            }

            if (_pcmBits != 0)
            {
                var raw = new byte[frames * _blockAlign];
                var read = 0;
                while (read < raw.Length)
                {
                    var n = _reader.Read(raw, read, raw.Length - read);
                    if (n <= 0) break;
                    read += n;
                }

                read -= read % _blockAlign;
                if (read <= 0) return [];
                _next += read / _blockAlign;
                return PcmMath.Widen24(raw.AsSpan(0, read), _pcmBits);
            }

            var wanted = frames * Format.Channels;
            var floats = new float[wanted];
            var got = _samples!.Read(floats, 0, wanted);
            got -= got % Format.Channels;
            if (got <= 0) return [];
            _next += got / Format.Channels;

            // 계산을 거치지 않은 신호다. 같은 눈금으로 되돌리면 정수 원본은 한 비트도 달라지지 않는다.
            return PcmMath.FromFloatExact(floats.AsSpan(0, got), 24);
        }
    }

    public void Dispose()
    {
        lock (_gate) _reader.Dispose();
    }
}
