using Mono.Shared;
using NAudio.Wave;

namespace Mono.Core;

public readonly record struct AudioFormat(int SampleRate, int BitDepth, int Channels, bool IsDsd)
{
    public int BytesPerFrame => IsDsd ? Channels : Channels * (BitDepth / 8);
}

/// <summary>
/// 트랙 한 개를 media_time 구간 단위로 잘라 주는 디코더 어댑터.
/// WAV·FLAC·ALAC/MP3(MF)·DSF. MF는 구간만 디코드(전량 RAM 적재 없음).
/// </summary>
public interface ITrackSlicer : IDisposable
{
    AudioFormat Format { get; }
    long DurationMs { get; }
    byte[] Read(long startMs, int durationMs);
}

public static class AudioSourceFactory
{
    public static ITrackSlicer Open(Track track)
    {
        var path = track.LocalPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (ext is ".wav")
                    return new WavSlicer(path);

                if (ext is ".dsf")
                    return new DsfSlicer(path);

                // Windows Media Foundation: FLAC / ALAC(m4a) / MP3 / AAC / AIFF 등
                if (ext is ".flac" or ".mp3" or ".m4a" or ".aac" or ".aiff" or ".aif" or ".wma" or ".mp4" or ".alac")
                    return new MediaFoundationSlicer(path);
            }
            catch (Exception)
            {
                // 헤더가 깨졌으면 톤 소스로 폴백한다.
            }
        }

        return new ToneSlicer(track);
    }
}

/// <summary>실제 WAV(PCM) 슬라이서. 헤더는 한 번만 파싱한다.</summary>
public sealed class WavSlicer : ITrackSlicer
{
    private readonly FileStream _fs;
    private readonly WavHeader _header;
    private readonly PcmFrameCursor _cursor;

    public WavSlicer(string path)
    {
        _fs = File.OpenRead(path);
        var header = WavHeader.Parse(_fs);
        if (header is null)
        {
            _fs.Dispose();
            throw new InvalidDataException("WAV 헤더를 읽지 못했습니다: " + path);
        }

        _header = header;
        _cursor = new PcmFrameCursor(header.SampleRate, header.DataSize / header.SourceBytesPerFrame);

        // 뎁스는 장치가 실제로 여는 16/24 로 접어서 알린다. 파일이 8·32bit 라고 해서 그 숫자를
        // 그대로 흘려보내면 바이트 수와 뎁스가 어긋나 아래층 전체가 잡음이 된다.
        Format = new AudioFormat(header.SampleRate, header.DeviceBits, header.Channels, false);
        DurationMs = header.DurationMs;
    }

    public AudioFormat Format { get; }
    public long DurationMs { get; }

    public byte[] Read(long startMs, int durationMs)
    {
        var bpf = _header.SourceBytesPerFrame;
        if (bpf == 0 || _header.DataSize <= 0 || durationMs <= 0)
            return [];

        var (startFrame, frames) = _cursor.Advance(startMs, durationMs);
        if (frames <= 0)
            return [];

        var buffer = new byte[frames * bpf];
        _fs.Position = _header.DataPosition + startFrame * bpf;
        var read = 0;
        while (read < buffer.Length)
        {
            var n = _fs.Read(buffer, read, buffer.Length - read);
            if (n <= 0) break;
            read += n;
        }

        if (read <= 0) return [];
        if (read < buffer.Length) buffer = buffer[..(read / bpf * bpf)];

        return PcmSamples.ToDeviceDepth(buffer, _header.SourceBits, _header.Encoding, out _);
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>
/// Media Foundation 구간 디코더. 파일을 전량 RAM에 올리지 않고 startMs부터 duration만 읽는다.
/// </summary>
public sealed class MediaFoundationSlicer : ITrackSlicer
{
    private readonly AudioFileReader _reader;
    private readonly object _gate = new();
    private long _cursorMs = -1;

    public MediaFoundationSlicer(string path)
    {
        _reader = new AudioFileReader(path);
        Format = new AudioFormat(
            _reader.WaveFormat.SampleRate,
            24,
            Math.Max(_reader.WaveFormat.Channels, 1),
            false);
        DurationMs = Math.Max(0, (long)_reader.TotalTime.TotalMilliseconds);
    }

    public AudioFormat Format { get; }
    public long DurationMs { get; }

    public byte[] Read(long startMs, int durationMs)
    {
        if (durationMs <= 0 || Format.SampleRate <= 0)
            return [];

        lock (_gate)
        {
            var target = Math.Clamp(startMs, 0, Math.Max(0, DurationMs - 1));
            // 연속 재생이면 seek 생략(디코더 재동기화 비용 절감)
            if (_cursorMs < 0 || Math.Abs(_cursorMs - target) > 80)
            {
                try { _reader.CurrentTime = TimeSpan.FromMilliseconds(target); }
                catch { /* 일부 포맷은 근사 seek */ }
            }

            var frames = Math.Max(1, durationMs * Format.SampleRate / 1000);
            var samplesNeeded = frames * Format.Channels;
            var floatBuf = new float[samplesNeeded];
            var read = _reader.Read(floatBuf, 0, samplesNeeded);
            _cursorMs = target + durationMs;
            if (read <= 0)
                return [];

            if (read < samplesNeeded)
                Array.Resize(ref floatBuf, read);
            return DspPipeline.FromFloat(floatBuf, 24);
        }
    }

    public void Dispose()
    {
        lock (_gate) _reader.Dispose();
    }
}

/// <summary>DSF 원본 DSD 비트스트림 패스스루. PCM 변환을 하지 않는다.</summary>
public sealed class DsfSlicer : ITrackSlicer
{
    private readonly FileStream _fs;
    private readonly long _dataPos;
    private readonly long _dataSize;
    private readonly int _blockSize;

    public DsfSlicer(string path)
    {
        _fs = File.OpenRead(path);
        using var br = new BinaryReader(_fs, System.Text.Encoding.ASCII, leaveOpen: true);
        var magic = new string(br.ReadChars(4));
        if (magic != "DSD ")
            throw new InvalidDataException("not a DSF file");

        br.ReadInt64();
        br.ReadInt64();
        br.ReadInt64();
        var fmtId = new string(br.ReadChars(4));
        if (fmtId != "fmt ")
            throw new InvalidDataException("missing fmt chunk");

        var fmtSize = br.ReadInt64();
        br.ReadInt32();
        br.ReadInt32();
        br.ReadInt32();
        var channels = br.ReadInt32();
        var rate = br.ReadInt32();
        var bits = br.ReadInt32();
        var sampleCount = br.ReadInt64();
        _blockSize = br.ReadInt32();
        br.ReadInt32();
        _fs.Position = 28 + fmtSize;
        var dataId = new string(br.ReadChars(4));
        var dataSize = br.ReadInt64();
        _dataPos = _fs.Position;
        _dataSize = dataId == "data" ? Math.Min(dataSize - 12, _fs.Length - _dataPos) : _fs.Length - _dataPos;
        Format = new AudioFormat(rate, bits, Math.Max(channels, 1), true);
        DurationMs = rate == 0 ? 0 : sampleCount * 1000 / rate;
    }

    public AudioFormat Format { get; }
    public long DurationMs { get; }

    public byte[] Read(long startMs, int durationMs)
    {
        var bytesPerMsPerChannel = Format.SampleRate / 8.0 / 1000.0;
        var blockBytes = (long)_blockSize * Format.Channels;
        if (blockBytes <= 0 || _dataSize <= 0)
            return [];

        var startByte = (long)(startMs * bytesPerMsPerChannel * Format.Channels);
        startByte -= startByte % blockBytes;
        startByte = Math.Clamp(startByte, 0, _dataSize);
        var want = (long)(durationMs * bytesPerMsPerChannel * Format.Channels);
        want = Math.Max(blockBytes, want - want % blockBytes);
        want = Math.Min(want, _dataSize - startByte);
        if (want <= 0)
            return [];

        var buffer = new byte[want];
        _fs.Position = _dataPos + startByte;
        var read = _fs.Read(buffer, 0, (int)want);
        return read == want ? buffer : buffer[..Math.Max(read, 0)];
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>파일이 없는 트랙(스트리밍/데모)용 합성 소스. 트랙마다 음고가 다르다.</summary>
public sealed class ToneSlicer : ITrackSlicer
{
    private readonly string _trackId;

    public ToneSlicer(Track track)
    {
        _trackId = track.Id;
        var depth = track.IsDsd ? 24 : Math.Max(track.BitDepth, 16);
        var rate = track.IsDsd ? 96000 : track.SampleRate;
        Format = new AudioFormat(rate, depth == 16 ? 16 : 24, Math.Max(track.Channels, 2), false);
        DurationMs = track.DurationMs > 0 ? track.DurationMs : 180000;
    }

    public AudioFormat Format { get; }
    public long DurationMs { get; }

    public byte[] Read(long startMs, int durationMs)
        => DemoPcm.ToneChunk(_trackId, Format.SampleRate, Format.BitDepth, Format.Channels, startMs, durationMs);

    public void Dispose() { }
}
