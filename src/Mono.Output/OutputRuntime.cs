using Mono.Protocol;
using Mono.Shared;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Mono.Output;

/// <summary>Core가 방송한 재생 타임라인의 로컬 사본.</summary>
public sealed class Timeline
{
    private long _originUnixMs;
    private long _timeAtOriginMs;

    public bool Playing { get; private set; }
    public long Epoch { get; private set; }
    public string? TrackId { get; private set; }
    public string? LocalPath { get; private set; }
    public long DurationMs { get; private set; }
    public bool ClockSyncMode { get; private set; }

    public void Apply(MonoMessage msg)
    {
        Playing = msg.Playing ?? Playing;
        _originUnixMs = msg.MediaOriginUnixMs ?? _originUnixMs;
        _timeAtOriginMs = msg.MediaTimeAtOriginMs ?? _timeAtOriginMs;
        Epoch = msg.Epoch ?? Epoch;
        DurationMs = msg.DurationMs ?? DurationMs;
        if (msg.Type == MessageTypes.RoomState)
        {
            TrackId = msg.TrackId;
            LocalPath = msg.LocalPath;
        }

        if (msg.SourceMode is { } mode)
        {
            ClockSyncMode = mode == PlaybackSourceMode.ClockSync;
        }
    }

    /// <summary>현재 media_time. offset은 (Core 시계 − 로컬 시계).</summary>
    public long MediaTimeMs(double offsetMs)
    {
        if (!Playing)
        {
            return _timeAtOriginMs;
        }

        var coreNow = ClockSync.UnixMs() + (long)Math.Round(offsetMs);
        return _timeAtOriginMs + (coreNow - _originUnixMs);
    }

    /// <summary>이 PTS를 로컬 시계 기준 언제 내보내야 하는가.</summary>
    public long PlayAtLocalUnixMs(long ptsMs, double offsetMs, int bufferMs)
        => ClockSync.PlayAtUnixMs(_originUnixMs, _timeAtOriginMs, ptsMs, bufferMs) - (long)Math.Round(offsetMs);
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

/// <summary>WASAPI / ASIO 공통 출력 백엔드.</summary>

/// <summary>
/// Clock-sync 모드에서 이 엔드포인트가 자기 로컬 파일을 직접 여는 경로.
/// (파일은 네트워크로 오지 않는다 — 라이선스 분기 A)
/// </summary>
public sealed class LocalFileRenderer : ILocalChunkSource
{
    private readonly FileStream _fs;
    private readonly long _dataPos;
    private readonly long _dataSize;
    private readonly int _rate;
    private readonly int _depth;
    private readonly int _channels;
    private long _cursorMs = -1;

    internal LocalFileRenderer(FileStream fs, long dataPos, long dataSize, int rate, int depth, int channels)
    {
        _fs = fs;
        _dataPos = dataPos;
        _dataSize = dataSize;
        _rate = rate;
        _depth = depth;
        _channels = channels;
    }

    public static ILocalChunkSource? TryOpen(string path)
    {
        if (!File.Exists(path))
            return null;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".flac" or ".mp3" or ".m4a" or ".aac" or ".aiff" or ".aif" or ".wma" or ".mp4" or ".alac")
            return MfStreamingLocalRenderer.TryOpen(path);

        if (ext is ".dsf")
            return DsfLocalRenderer.TryOpen(path);

        if (!ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs, System.Text.Encoding.ASCII, leaveOpen: true);
            br.ReadBytes(12);
            int rate = 44100, depth = 16, channels = 2;
            while (fs.Position < fs.Length - 8)
            {
                var id = new string(br.ReadChars(4));
                var size = br.ReadUInt32();
                if (id == "fmt ")
                {
                    br.ReadInt16();
                    channels = br.ReadInt16();
                    rate = br.ReadInt32();
                    br.ReadInt32();
                    br.ReadInt16();
                    depth = br.ReadInt16();
                    if (size > 16)
                        br.ReadBytes((int)size - 16);
                }
                else if (id == "data")
                {
                    return new LocalFileRenderer(fs, fs.Position, Math.Min(size, fs.Length - fs.Position), rate, depth, channels);
                }
                else
                {
                    fs.Position += size;
                }
            }

            fs.Dispose();
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    /// <summary>media_time에서 durationMs만큼. 이미 보낸 구간은 건너뛴다.</summary>
    public byte[] Read(long mediaTimeMs, int durationMs, out (int rate, int depth, int channels) format)
    {
        format = (_rate, _depth, _channels);
        var start = _cursorMs < 0 || Math.Abs(_cursorMs - mediaTimeMs) > 500 ? mediaTimeMs : _cursorMs;
        var bpf = _channels * (_depth / 8);
        var startByte = Math.Clamp(start * _rate / 1000 * bpf, 0, _dataSize);
        var want = (int)Math.Min((long)durationMs * _rate / 1000 * bpf, _dataSize - startByte);
        if (want <= 0)
            return [];

        var buffer = new byte[want];
        _fs.Position = _dataPos + startByte;
        var read = _fs.Read(buffer, 0, want);
        _cursorMs = start + durationMs;
        return read == want ? buffer : buffer[..Math.Max(read, 0)];
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>FLAC/MP3 등 Media Foundation 구간 디코드 (Clock-sync 로컬). 전량 RAM 적재 없음.</summary>
file sealed class MfStreamingLocalRenderer : ILocalChunkSource
{
    private readonly AudioFileReader _reader;
    private readonly object _gate = new();
    private readonly int _rate;
    private readonly int _channels;
    private long _cursorMs = -1;

    private MfStreamingLocalRenderer(AudioFileReader reader)
    {
        _reader = reader;
        _rate = reader.WaveFormat.SampleRate;
        _channels = Math.Max(reader.WaveFormat.Channels, 1);
    }

    public static MfStreamingLocalRenderer? TryOpen(string path)
    {
        try { return new MfStreamingLocalRenderer(new AudioFileReader(path)); }
        catch { return null; }
    }

    public byte[] Read(long mediaTimeMs, int durationMs, out (int rate, int depth, int channels) format)
    {
        format = (_rate, 24, _channels);
        lock (_gate)
        {
            var target = Math.Max(0, mediaTimeMs);
            if (_cursorMs < 0 || Math.Abs(_cursorMs - target) > 80)
            {
                try { _reader.CurrentTime = TimeSpan.FromMilliseconds(target); }
                catch { /* approx */ }
            }

            var frames = Math.Max(1, durationMs * _rate / 1000);
            var samplesNeeded = frames * _channels;
            var floatBuf = new float[samplesNeeded];
            var n = _reader.Read(floatBuf, 0, samplesNeeded);
            _cursorMs = target + durationMs;
            if (n <= 0) return [];
            if (n < samplesNeeded) Array.Resize(ref floatBuf, n);
            return FloatToPcm24(floatBuf);
        }
    }

    private static byte[] FloatToPcm24(float[] samples)
    {
        var bytes = new byte[samples.Length * 3];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (int)Math.Clamp(samples[i] * 8388607f, -8388608, 8388607);
            bytes[i * 3] = (byte)v;
            bytes[i * 3 + 1] = (byte)(v >> 8);
            bytes[i * 3 + 2] = (byte)(v >> 16);
        }
        return bytes;
    }

    public void Dispose()
    {
        lock (_gate) _reader.Dispose();
    }
}

/// <summary>DSF → DoP 로컬 Clock-sync 경로 (채널 인터리브 검증용).</summary>
file sealed class DsfLocalRenderer : ILocalChunkSource
{
    private readonly FileStream _fs;
    private readonly long _dataPos;
    private readonly long _dataSize;
    private readonly int _blockSize;
    private readonly int _dsdRate;
    private readonly int _channels;
    private long _cursorMs = -1;

    private DsfLocalRenderer(FileStream fs, long dataPos, long dataSize, int blockSize, int dsdRate, int channels)
    {
        _fs = fs;
        _dataPos = dataPos;
        _dataSize = dataSize;
        _blockSize = blockSize;
        _dsdRate = dsdRate;
        _channels = channels;
    }

    public static DsfLocalRenderer? TryOpen(string path)
    {
        try
        {
            var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs, System.Text.Encoding.ASCII, leaveOpen: true);
            if (new string(br.ReadChars(4)) != "DSD ") { fs.Dispose(); return null; }
            br.ReadInt64(); br.ReadInt64(); br.ReadInt64();
            if (new string(br.ReadChars(4)) != "fmt ") { fs.Dispose(); return null; }
            var fmtSize = br.ReadInt64();
            br.ReadInt32(); br.ReadInt32(); br.ReadInt32();
            var channels = br.ReadInt32();
            var rate = br.ReadInt32();
            br.ReadInt32();
            br.ReadInt64();
            var blockSize = br.ReadInt32();
            br.ReadInt32();
            fs.Position = 28 + fmtSize;
            var dataId = new string(br.ReadChars(4));
            var dataSize = br.ReadInt64();
            var dataPos = fs.Position;
            var size = dataId == "data" ? Math.Min(dataSize - 12, fs.Length - dataPos) : fs.Length - dataPos;
            return new DsfLocalRenderer(fs, dataPos, size, blockSize, rate, Math.Max(channels, 1));
        }
        catch
        {
            return null;
        }
    }

    public byte[] Read(long mediaTimeMs, int durationMs, out (int rate, int depth, int channels) format)
    {
        var start = _cursorMs < 0 || Math.Abs(_cursorMs - mediaTimeMs) > 500 ? mediaTimeMs : _cursorMs;
        var bytesPerMsPerChannel = _dsdRate / 8.0 / 1000.0;
        var blockBytes = (long)_blockSize * _channels;
        var startByte = (long)(start * bytesPerMsPerChannel * _channels);
        if (blockBytes > 0) startByte -= startByte % blockBytes;
        startByte = Math.Clamp(startByte, 0, _dataSize);
        var want = (long)(durationMs * bytesPerMsPerChannel * _channels);
        if (blockBytes > 0) want = Math.Max(blockBytes, want - want % blockBytes);
        want = Math.Min(want, _dataSize - startByte);
        if (want <= 0)
        {
            format = (176400, 24, _channels);
            return [];
        }

        var dsd = new byte[want];
        _fs.Position = _dataPos + startByte;
        var read = _fs.Read(dsd, 0, (int)want);
        if (read < want) Array.Resize(ref dsd, Math.Max(read, 0));
        _cursorMs = start + durationMs;
        var encoded = DopEncoder.Encode(dsd, _dsdRate, _channels);
        format = (encoded.Rate, encoded.Depth, encoded.Channels);
        return encoded.Pcm;
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>Clock-sync 로컬 소스 공용 인터페이스.</summary>
public interface ILocalChunkSource : IDisposable
{
    byte[] Read(long mediaTimeMs, int durationMs, out (int rate, int depth, int channels) format);
}
