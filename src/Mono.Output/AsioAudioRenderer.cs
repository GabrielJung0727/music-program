using NAudio.Wave;

namespace Mono.Output;

/// <summary>
/// ASIO 출력 백엔드. 드라이버가 없거나 초기화 실패 시 null.
/// 네이티브 DSD는 드라이버 지원 시에만 — 여기선 PCM/DoP 프레임을 ASIO로 전달.
/// </summary>
public sealed class AsioAudioRenderer : IAudioRenderer
{
    private readonly AsioOut _asio;
    private readonly object _gate = new();
    private BufferedWaveProvider? _buffer;

    private AsioAudioRenderer(AsioOut asio, string name)
    {
        _asio = asio;
        DeviceName = "ASIO · " + name;
        MaxSampleRate = 192000;
        MaxBitDepth = 24;
        HardwareVolume = false;
        LatencyMs = 5;
        Exclusive = true;
    }

    public static AsioAudioRenderer? TryCreate(string? deviceHint)
    {
        try
        {
            var drivers = AsioOut.GetDriverNames();
            if (drivers.Length == 0) return null;
            var name = deviceHint is null
                ? drivers[0]
                : drivers.FirstOrDefault(d => d.Contains(deviceHint, StringComparison.OrdinalIgnoreCase)) ?? drivers[0];
            var asio = new AsioOut(name);
            return new AsioAudioRenderer(asio, name);
        }
        catch
        {
            return null;
        }
    }

    public string DeviceName { get; }
    public int MaxSampleRate { get; }
    public int MaxBitDepth { get; }
    public bool HardwareVolume { get; }
    public long LatencyMs { get; private set; }
    public bool Exclusive { get; }
    public bool Playing => _asio.PlaybackState == PlaybackState.Playing;

    public double BufferedMs
    {
        get
        {
            lock (_gate) return _buffer?.BufferedDuration.TotalMilliseconds ?? 0;
        }
    }

    public void Push(byte[] pcm, int rate, int depth, int channels, int volumePercent)
    {
        if (pcm.Length == 0) return;
        lock (_gate)
        {
            var bits = depth >= 24 ? 24 : 16;
            var ch = Math.Max(channels, 1);
            if (_buffer is null
                || _buffer.WaveFormat.SampleRate != rate
                || _buffer.WaveFormat.BitsPerSample != bits
                || _buffer.WaveFormat.Channels != ch)
            {
                Open(rate, bits, ch);
            }

            var payload = pcm;
            if (volumePercent < 100)
                payload = Attenuate(pcm, bits, volumePercent);
            _buffer!.AddSamples(payload, 0, payload.Length);
        }
    }

    public void Flush()
    {
        lock (_gate) _buffer?.ClearBuffer();
    }

    public void SetHardwareVolume(int percent) { /* ASIO: 앱 디지털 감쇠 */ }

    private void Open(int rate, int bits, int channels)
    {
        try
        {
            if (_asio.PlaybackState != PlaybackState.Stopped)
                _asio.Stop();
        }
        catch { /* ignore */ }

        var format = bits >= 24
            ? WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, rate, channels, rate * channels * 3, channels * 3, 24)
            : new WaveFormat(rate, 16, channels);
        _buffer = new BufferedWaveProvider(format)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(200)
        };
        _asio.Init(_buffer);
        _asio.Play();
        LatencyMs = Math.Max(3, _asio.PlaybackLatency);
    }

    private static byte[] Attenuate(byte[] pcm, int bits, int percent)
    {
        var gain = Math.Clamp(percent, 0, 100) / 100f;
        var copy = (byte[])pcm.Clone();
        if (bits == 16)
        {
            for (var i = 0; i + 1 < copy.Length; i += 2)
            {
                var s = (short)(BitConverter.ToInt16(copy, i) * gain);
                BitConverter.TryWriteBytes(copy.AsSpan(i), s);
            }
            return copy;
        }

        for (var i = 0; i + 2 < copy.Length; i += 3)
        {
            var v = copy[i] | (copy[i + 1] << 8) | ((sbyte)copy[i + 2] << 16);
            v = (int)(v * gain);
            copy[i] = (byte)v;
            copy[i + 1] = (byte)(v >> 8);
            copy[i + 2] = (byte)(v >> 16);
        }
        return copy;
    }

    public void Dispose()
    {
        try { _asio.Stop(); } catch { /* ignore */ }
        _asio.Dispose();
    }
}
