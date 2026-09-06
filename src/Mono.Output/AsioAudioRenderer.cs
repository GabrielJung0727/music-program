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

    /// <summary>장치 큐가 담을 수 있는 최대 깊이.</summary>
    public int CapacityMs => 500;

    private readonly AsioOut _asio;
    private readonly object _gate = new();
    private AsioStaHost? _staHost;
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

    public static AsioAudioRenderer? TryCreate(string? deviceHint) => TryCreate(deviceHint, out _);

    /// <summary>
    /// ASIO 드라이버를 연다. 실패하면 사유를 돌려준다 — 사유를 삼키면 드라이버가 설치돼
    /// 있는데도 왜 안 열리는지 알 수 없다.
    ///
    /// ASIO 드라이버는 COM 개체라 STA 아파트먼트를 요구하는 것이 많다. 이 프로세스의
    /// 주 스레드는 MTA 이므로 전용 STA 스레드에서 만들고, 그 스레드를 개체 수명 동안
    /// 살려 둔 채 메시지를 펌프한다. 스레드가 먼저 끝나면 아파트먼트가 사라져 개체가 죽는다.
    /// </summary>
    public static AsioAudioRenderer? TryCreate(string? deviceHint, out string? error)
    {
        error = null;
        string[] drivers;
        try
        {
            drivers = AsioOut.GetDriverNames();
        }
        catch (Exception ex)
        {
            error = "드라이버 목록을 읽지 못했습니다 — " + ex.Message;
            return null;
        }

        if (drivers.Length == 0)
        {
            error = "설치된 ASIO 드라이버가 없습니다.";
            return null;
        }

        var name = deviceHint is null
            ? drivers[0]
            : drivers.FirstOrDefault(d => d.Contains(deviceHint, StringComparison.OrdinalIgnoreCase)) ?? drivers[0];

        var host = new AsioStaHost(name);
        if (host.Driver is null)
        {
            error = $"{name}: {host.Failure ?? "응답 없음"}";
            host.Dispose();
            return null;
        }

        return new AsioAudioRenderer(host.Driver, name) { _staHost = host };
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
                    BufferDuration = TimeSpan.FromMilliseconds(CapacityMs)
                };
                _asio.Init(_buffer);

                // PlaybackLatency 는 "샘플" 단위다. 밀리초로 착각해서 쓰면 지연이 수백 ms 로
                // 뻥튀기되고, 그만큼 백프레셔 천장이 버퍼 용량을 넘어서 버퍼가 넘치는 쪽으로
                // 오디오가 조용히 버려진다(딸깍 소리).
                var latencySamples = _asio.PlaybackLatency;
                LatencyMs = Math.Max(3, (long)Math.Round(latencySamples * 1000.0 / Math.Max(1, config.SampleRate)));
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
        _staHost?.Dispose();
    }
}

/// <summary>
/// ASIO 드라이버 COM 개체를 만들고 살려 두는 STA 스레드.
/// 만든 스레드가 끝나면 아파트먼트가 사라져 개체가 함께 죽으므로, 이 스레드는
/// 개체를 쓰는 동안 계속 살아 있어야 한다.
/// </summary>
internal sealed class AsioStaHost : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _stop = new(false);

    public AsioOut? Driver { get; private set; }
    public string? Failure { get; private set; }

    public AsioStaHost(string driverName)
    {
        _thread = new Thread(() =>
        {
            try { Driver = new AsioOut(driverName); }
            catch (Exception ex) { Failure = $"{ex.GetType().Name}: {ex.Message}"; }
            finally { _ready.Set(); }

            // 아파트먼트를 유지한다. Dispose 될 때까지 이 스레드는 살아 있어야 한다.
            _stop.Wait();
        })
        {
            IsBackground = true,
            Name = "mono-asio-sta"
        };

        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }

        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
        {
            Failure = "드라이버가 10초 안에 응답하지 않았습니다.";
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join(1000);
        _ready.Dispose();
        _stop.Dispose();
    }
}
