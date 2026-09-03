using Mono.Shared;

namespace Mono.Core;

public readonly record struct AudioFormat(int SampleRate, int BitDepth, int Channels, bool IsDsd)
{
    public int BytesPerFrame => IsDsd ? Channels : Channels * (BitDepth / 8);
}

/// <summary>
/// 트랙 한 개를 media_time 구간 단위로 잘라 주는 디코더 어댑터.
/// WAV·FLAC·ALAC/MP3(MF)·DSF 경로가 있다. MF는 전량 메모리 디코드.
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
                {
                    return new WavSlicer(path);
                }

                if (ext is ".dsf")
                {
                    return new DsfSlicer(path);
                }

                // Windows Media Foundation: FLAC / ALAC(m4a) / MP3 / AAC / AIFF 등
                if (ext is ".flac" or ".mp3" or ".m4a" or ".aac" or ".aiff" or ".aif" or ".wma" or ".mp4")
                {
                    return new MediaFoundationSlicer(path);
                }
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
    private readonly long _dataPos;
    private readonly long _dataSize;

    public WavSlicer(string path)
    {
        _fs = File.OpenRead(path);
        using var br = new BinaryReader(_fs, System.Text.Encoding.ASCII, leaveOpen: true);
        br.ReadBytes(12);
        var rate = 44100;
        var depth = 16;
        var channels = 2;
        while (_fs.Position < _fs.Length - 8)
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
                {
                    br.ReadBytes((int)size - 16);
                }
            }
            else if (id == "data")
            {
                _dataPos = _fs.Position;
                _dataSize = Math.Min(size, _fs.Length - _dataPos);
                break;
            }
            else
            {
                _fs.Position += size;
            }
        }

        Format = new AudioFormat(rate, depth, Math.Max(channels, 1), false);
        DurationMs = Format.BytesPerFrame == 0 ? 0 : _dataSize * 1000 / (Format.BytesPerFrame * (long)rate);
    }

    public AudioFormat Format { get; }

    public long DurationMs { get; }

    public byte[] Read(long startMs, int durationMs)
    {
        var bpf = Format.BytesPerFrame;
        if (bpf == 0 || _dataSize <= 0)
        {
            return [];
        }

        var startByte = Math.Clamp(startMs * Format.SampleRate / 1000 * bpf, 0, _dataSize);
        var want = (int)Math.Min((long)durationMs * Format.SampleRate / 1000 * bpf, _dataSize - startByte);
        if (want <= 0)
        {
            return [];
        }

        var buffer = new byte[want];
        _fs.Position = _dataPos + startByte;
        var read = 0;
        while (read < want)
        {
            var n = _fs.Read(buffer, read, want - read);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        return read == want ? buffer : buffer[..read];
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>
/// NAudio Media Foundation 디코더. FLAC/MP3/M4A 등을 PCM으로 풀어 구간 슬라이스한다.
/// </summary>
public sealed class MediaFoundationSlicer : ITrackSlicer
{
    private readonly byte[] _pcm;
    private readonly int _bytesPerFrame;

    public MediaFoundationSlicer(string path)
    {
        using var reader = new NAudio.Wave.AudioFileReader(path);
        var rate = reader.WaveFormat.SampleRate;
        var channels = reader.WaveFormat.Channels;
        var samples = new List<float>(rate * channels * 8);
        var buf = new float[rate * channels / 5];
        int read;
        while ((read = reader.Read(buf, 0, buf.Length)) > 0)
        {
            for (var i = 0; i < read; i++) samples.Add(buf[i]);
        }

        var f = samples.ToArray();
        _pcm = DspPipeline.FromFloat(f, 24);
        Format = new AudioFormat(rate, 24, Math.Max(channels, 1), false);
        _bytesPerFrame = Format.BytesPerFrame;
        DurationMs = rate == 0 || _bytesPerFrame == 0
            ? 0
            : _pcm.Length * 1000L / (_bytesPerFrame * (long)rate);
    }

    public AudioFormat Format { get; }
    public long DurationMs { get; }

    public byte[] Read(long startMs, int durationMs)
    {
        if (_bytesPerFrame == 0 || _pcm.Length == 0) return [];
        var start = (int)Math.Clamp(startMs * Format.SampleRate / 1000 * _bytesPerFrame, 0, _pcm.Length);
        var want = (int)Math.Min((long)durationMs * Format.SampleRate / 1000 * _bytesPerFrame, _pcm.Length - start);
        if (want <= 0) return [];
        var slice = new byte[want];
        Buffer.BlockCopy(_pcm, start, slice, 0, want);
        return slice;
    }

    public void Dispose() { }
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
        {
            throw new InvalidDataException("not a DSF file");
        }

        br.ReadInt64();  // chunk size
        br.ReadInt64();  // total file size
        br.ReadInt64();  // metadata pointer
        var fmtId = new string(br.ReadChars(4));
        if (fmtId != "fmt ")
        {
            throw new InvalidDataException("missing fmt chunk");
        }

        var fmtSize = br.ReadInt64();
        br.ReadInt32();  // format version
        br.ReadInt32();  // format id
        br.ReadInt32();  // channel type
        var channels = br.ReadInt32();
        var rate = br.ReadInt32();
        var bits = br.ReadInt32();
        var sampleCount = br.ReadInt64();
        _blockSize = br.ReadInt32();
        br.ReadInt32();  // reserved
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

    /// <summary>블록 경계에 맞춰 자른다. DSD는 채널별 블록 인터리브다.</summary>
    public byte[] Read(long startMs, int durationMs)
    {
        var bytesPerMsPerChannel = Format.SampleRate / 8.0 / 1000.0;
        var blockBytes = (long)_blockSize * Format.Channels;
        if (blockBytes <= 0 || _dataSize <= 0)
        {
            return [];
        }

        var startByte = (long)(startMs * bytesPerMsPerChannel * Format.Channels);
        startByte -= startByte % blockBytes;
        startByte = Math.Clamp(startByte, 0, _dataSize);
        var want = (long)(durationMs * bytesPerMsPerChannel * Format.Channels);
        want = Math.Max(blockBytes, want - want % blockBytes);
        want = Math.Min(want, _dataSize - startByte);
        if (want <= 0)
        {
            return [];
        }

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
