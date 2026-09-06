using NAudio.Wave;

namespace Mono.Output;

/// <summary>
/// ASIO 출력 엔드포인트. 드라이버가 없거나 초기화에 실패하면 null.
/// 네이티브 DSD는 드라이버가 지원할 때만 — 여기서는 PCM/DoP 프레임을 ASIO로 넘긴다.
///
/// ASIO 는 본질적으로 배타 점유다. 초기화에 실패하면 공유 모드 같은 대안이 없으므로
/// 상태만 남기고 멈춘다. 일부 구형 드라이버는 빠른 재초기화에서 내부 락을 놓지 않으므로,
/// 정지 후에도 핸들을 잠시 유지해 재초기화 자체를 줄인다.
/// </summary>
public sealed class AsioAudioRenderer : IAudioOutputDevice
{
    /// <summary>정지 후 핸들을 붙들고 있는 시간.</summary>
    private const int ReleaseHoldMs = 3000;

    private readonly AsioOut _asio;
    private readonly object _gate = new();
    private BufferedWaveProvider? _buffer;
    private AudioDeviceState _state = AudioDeviceState.Idle;
    private long _releaseAtUnixMs;
    private readonly FrameAligner _aligner = new();

    private AsioAudioRenderer(AsioOut asio, string name)
    {
        _asio = asio;
        DeviceName = "ASIO · " + name;
        HardwareVolume = false;
        LatencyMs = 5;
        Exclusive = true;
    }

    /// <summary>설치된 ASIO 드라이버 이름. 없으면 빈 배열.</summary>
    public static string[] DriverNames()
    {
        try { return AsioOut.GetDriverNames(); }
        catch { return []; }
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
    public bool HardwareVolume { get; }
    public long LatencyMs { get; private set; }
    public int PrimeMs { get; set; } = 50;
    public bool Exclusive { get; }
    public string? LastError { get; private set; }

    public bool Playing => _asio.PlaybackState == PlaybackState.Playing;

    public double BufferedMs
    {
        get { lock (_gate) return _buffer?.BufferedDuration.TotalMilliseconds ?? 0; }
    }

    public AudioDeviceState GetCurrentState()
    {
        lock (_gate)
        {
            ExpireReleaseWait();
            return _state;
        }
    }

    public Capabilities GetSupportedFormats()
    {
        // ASIO 드라이버는 자기 규격을 협상으로만 알려 준다. 흔히 쓰는 범위를 보고한다.
        return new Capabilities(
            DeviceName,
            [44100, 48000, 88200, 96000, 176400, 192000],
            [24],
            2,
            SupportsExclusive: true,
            HardwareVolume: false);
    }

    public bool OpenDevice(DeviceConfig config)
    {
        lock (_gate)
        {
            if (_state == AudioDeviceState.ReleaseWait && _buffer is not null && SameFormat(config))
            {
                _state = AudioDeviceState.ExclusiveStreaming;
                _releaseAtUnixMs = 0;
                return true;
            }

            _state = AudioDeviceState.Initializing;
            LastError = null;

            try
            {
                if (_asio.PlaybackState != PlaybackState.Stopped) _asio.Stop();
            }
            catch
            {
                // 이미 멈춘 드라이버면 무시
            }

            try
            {
                _aligner.Reset();
                _buffer = new BufferedWaveProvider(
                    BuildFormat(config.SampleRate, config.BitDepth, config.Channels))
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(200)
                };
                _asio.Init(_buffer);
                LatencyMs = Math.Max(3, _asio.PlaybackLatency);
                _state = AudioDeviceState.ExclusiveStreaming;
                return true;
            }
            catch (Exception ex)
            {
                _buffer = null;
                _state = AudioDeviceState.DeviceBusyLocked;
                LastError = $"ASIO 드라이버를 {config.SampleRate}Hz/{config.BitDepth}bit 로 열지 못했습니다 — {ex.Message}";
                return false;
            }
        }
    }

    public bool PushSamples(AudioBuffer buffer, int volumePercent)
    {
        if (buffer.Count <= 0) return false;

        lock (_gate)
        {
            ExpireReleaseWait();
            if (_state is AudioDeviceState.DeviceLostSuspend or AudioDeviceState.DeviceBusyLocked)
            {
                return false;
            }

            var bits = buffer.BitDepth >= 24 ? 24 : 16;
            var channels = Math.Max(buffer.Channels, 1);
            if (_buffer is null || !SameFormat(buffer.SampleRate, bits, channels))
            {
                var config = new DeviceConfig(
                    buffer.SampleRate, bits, channels, buffer.IsDsd, DeviceMode.BitPerfectExclusive);
                if (!OpenDevice(config)) return false;
            }

            var payload = new ReadOnlySpan<byte>(buffer.Data, buffer.Offset, buffer.Count).ToArray();
            if (volumePercent < 100)
            {
                payload = Attenuate(payload, bits, volumePercent);
            }

            // 온전한 프레임만 넘긴다. 남는 꼬리는 다음 청크 앞에 붙는다.
            var aligned = _aligner.Take(payload, _buffer!.WaveFormat.BlockAlign);
            if (aligned.Length == 0) return true;

            _buffer.AddSamples(aligned, 0, aligned.Length);

            // 빈 버퍼로 시작하면 첫 순간부터 언더런이다. 목표 깊이를 채운 뒤에 연다.
            if (_asio.PlaybackState != PlaybackState.Playing
                && _buffer.BufferedDuration.TotalMilliseconds >= PrimeMs)
            {
                _asio.Play();
            }

            return true;
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            _buffer?.ClearBuffer();
            _aligner.Reset();
            // 비운 직후 그대로 재생하면 빈 버퍼를 긁는다. 다시 프라임될 때까지 멈춘다.
            try { _asio.Pause(); } catch { /* 장치가 이미 닫혔으면 무시 */ }
        }
    }

    public void ReleaseDevice()
    {
        lock (_gate)
        {
            if (_buffer is null)
            {
                _state = AudioDeviceState.Idle;
                return;
            }

            try { _asio.Pause(); } catch { /* 무시 */ }
            _buffer.ClearBuffer();
            _aligner.Reset();
            _state = AudioDeviceState.ReleaseWait;
            _releaseAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ReleaseHoldMs;
        }
    }

    private void ExpireReleaseWait()
    {
        if (_state != AudioDeviceState.ReleaseWait) return;
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _releaseAtUnixMs) return;

        try { _asio.Stop(); } catch { /* 무시 */ }
        _buffer = null;
        _releaseAtUnixMs = 0;
        _state = AudioDeviceState.Idle;
    }

    public void SetHardwareVolume(int percent)
    {
        // ASIO: 앱 디지털 감쇠로만 처리한다.
    }

    private bool SameFormat(DeviceConfig c) => SameFormat(c.SampleRate, c.BitDepth, c.Channels);

    private bool SameFormat(int rate, int bits, int channels)
        => _buffer is not null
           && _buffer.WaveFormat.SampleRate == rate
           && _buffer.WaveFormat.BitsPerSample == bits
           && _buffer.WaveFormat.Channels == channels;

    private static WaveFormat BuildFormat(int rate, int bits, int channels)
        => bits >= 24
            ? WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, rate, channels, rate * channels * 3, channels * 3, 24)
            : new WaveFormat(rate, 16, channels);

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
        try { _asio.Stop(); } catch { /* 무시 */ }
        _asio.Dispose();
    }
}
