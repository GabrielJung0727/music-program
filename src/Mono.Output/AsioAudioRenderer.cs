using System.Collections.Concurrent;
using NAudio.Wave;

namespace Mono.Output;

/// <summary>
/// ASIO 출력 엔드포인트.
///
/// ASIO 는 본질적으로 배타 점유다. 초기화에 실패하면 공유 모드 같은 대안이 없으므로
/// 상태만 남기고 멈춘다.
///
/// 드라이버는 COM 개체라 세 가지를 지킨다:
///  - 만드는 것도 쓰는 것도 전부 같은 STA 스레드에서 한다(AsioStaHost).
///  - AsioOut 은 두 번 Init 할 수 없다. 포맷이 바뀌면 버리고 새로 만든다.
///  - 드라이버에는 32비트 float 로 넘긴다. NAudio 의 ASIO 변환기가 가장 넓게 지원하는
///    입력이고, Int24LSB 계열 드라이버는 16비트 입력을 아예 받지 않는다.
///    16/24비트 정수는 float32 에 정확히 담기므로 비트퍼펙트는 그대로다.
/// </summary>
public sealed class AsioAudioRenderer : IAudioOutputDevice
{
    /// <summary>정지 후 핸들을 붙들고 있는 시간.</summary>
    private const int ReleaseHoldMs = 3000;

    private readonly string _driverName;
    private readonly AsioStaHost _sta;
    private readonly object _gate = new();
    private readonly FrameAligner _aligner = new();

    private AsioOut? _asio;
    private BufferedWaveProvider? _buffer;
    private AudioDeviceState _state = AudioDeviceState.Idle;
    private long _releaseAtUnixMs;
    private int _sourceBitDepth = 16;

    private AsioAudioRenderer(AsioStaHost sta, string name)
    {
        _sta = sta;
        _driverName = name;
        DeviceName = "ASIO · " + name;
        HardwareVolume = false;
        LatencyMs = 5;
        Exclusive = true;
    }

    /// <summary>장치 큐가 담을 수 있는 최대 깊이.</summary>
    public int CapacityMs => 500;

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

        var sta = new AsioStaHost();
        if (sta.Failure is not null)
        {
            error = sta.Failure;
            sta.Dispose();
            return null;
        }

        // 실제로 열리는지 여기서 한 번 확인하고 바로 놓아준다. 포맷은 재생 시점에 정해진다.
        try
        {
            sta.Invoke(() =>
            {
                var probe = new AsioOut(name);
                probe.Dispose();
            });
        }
        catch (Exception ex)
        {
            error = $"{name}: {ex.GetType().Name}: {ex.Message}";
            sta.Dispose();
            return null;
        }

        return new AsioAudioRenderer(sta, name);
    }

    public string DeviceName { get; }
    public bool HardwareVolume { get; }
    public long LatencyMs { get; private set; }
    public int PrimeMs { get; set; } = 50;
    public bool Exclusive { get; }
    public string? LastError { get; private set; }

    public bool Playing => _asio?.PlaybackState == PlaybackState.Playing;

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
        => new(DeviceName,
            [44100, 48000, 88200, 96000, 176400, 192000],
            [16, 24],
            2,
            SupportsExclusive: true,
            HardwareVolume: false);

    public bool OpenDevice(DeviceConfig config)
    {
        lock (_gate)
        {
            if (_state == AudioDeviceState.ReleaseWait && _asio is not null && SameFormat(config))
            {
                _state = AudioDeviceState.ExclusiveStreaming;
                _releaseAtUnixMs = 0;
                return true;
            }

            _state = AudioDeviceState.Initializing;
            LastError = null;

            // AsioOut 은 두 번 Init 할 수 없다. 포맷이 바뀌면 통째로 새로 만든다.
            TearDown();

            try
            {
                _aligner.Reset();
                _sourceBitDepth = config.BitDepth >= 24 ? 24 : 16;

                // 드라이버에는 float32 로 넘긴다. 정수 폭이 좁으면 못 받는 드라이버가 있다.
                _buffer = new BufferedWaveProvider(
                    WaveFormat.CreateIeeeFloatWaveFormat(config.SampleRate, config.Channels))
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(CapacityMs)
                };

                var buffer = _buffer;
                _sta.Invoke(() =>
                {
                    var driver = new AsioOut(_driverName);
                    driver.Init(buffer);
                    _asio = driver;
                });

                // PlaybackLatency 는 "샘플" 단위다. 밀리초로 착각해서 쓰면 지연이 수백 ms 로
                // 뻥튀기되고, 그만큼 백프레셔 천장이 버퍼 용량을 넘어 오디오가 조용히 버려진다.
                var latencySamples = _sta.Invoke(() => _asio!.PlaybackLatency);
                LatencyMs = Math.Max(3, (long)Math.Round(latencySamples * 1000.0 / Math.Max(1, config.SampleRate)));

                Console.WriteLine(
                    $"asio open: {config.SampleRate}Hz {config.Channels}ch · 소스 {_sourceBitDepth}bit → float32 · " +
                    $"latency={latencySamples}samples({LatencyMs}ms)");

                _state = AudioDeviceState.ExclusiveStreaming;
                return true;
            }
            catch (Exception ex)
            {
                TearDown();
                _state = AudioDeviceState.DeviceBusyLocked;
                LastError = $"ASIO 드라이버를 {config.SampleRate}Hz/{config.BitDepth}bit 로 열지 못했습니다 — "
                          + $"{ex.GetType().Name}: {ex.Message}";
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

            // 정렬은 원본 PCM 기준으로 잡는다 — 반 프레임을 float 로 바꾸면 어긋남이 굳어진다.
            var aligned = _aligner.Take(payload, channels * (bits / 8));
            if (aligned.Length == 0) return true;

            var floats = ToFloatBytes(aligned, bits);
            _buffer!.AddSamples(floats, 0, floats.Length);

            // 빈 버퍼로 시작하면 첫 순간부터 언더런이다. 목표 깊이를 채운 뒤에 연다.
            if (_asio is not null
                && _asio.PlaybackState != PlaybackState.Playing
                && _buffer.BufferedDuration.TotalMilliseconds >= PrimeMs)
            {
                var driver = _asio;
                _sta.Invoke(() => driver.Play());
            }

            return true;
        }
    }

    /// <summary>16/24비트 정수 PCM → 32비트 float. 두 폭 모두 float32 에 정확히 담긴다.</summary>
    public static byte[] ToFloatBytes(byte[] pcm, int bitDepth)
    {
        if (bitDepth == 16)
        {
            var count = pcm.Length / 2;
            var outBytes = new byte[count * 4];
            for (var i = 0; i < count; i++)
            {
                var v = BitConverter.ToInt16(pcm, i * 2) / 32768f;
                BitConverter.TryWriteBytes(outBytes.AsSpan(i * 4), v);
            }

            return outBytes;
        }

        var samples = pcm.Length / 3;
        var bytes = new byte[samples * 4];
        for (var i = 0; i < samples; i++)
        {
            var v = pcm[i * 3] | (pcm[i * 3 + 1] << 8) | (pcm[i * 3 + 2] << 16);
            if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), v / 8388608f);
        }

        return bytes;
    }

    public void Flush()
    {
        lock (_gate)
        {
            _buffer?.ClearBuffer();
            _aligner.Reset();
            // 비운 직후 그대로 재생하면 빈 버퍼를 긁는다. 다시 프라임될 때까지 멈춘다.
            var driver = _asio;
            if (driver is null) return;
            try { _sta.Invoke(() => driver.Pause()); }
            catch { /* 장치가 이미 닫혔으면 무시 */ }
        }
    }

    public void ReleaseDevice()
    {
        lock (_gate)
        {
            var driver = _asio;
            if (driver is null)
            {
                _state = AudioDeviceState.Idle;
                return;
            }

            try { _sta.Invoke(() => driver.Pause()); } catch { /* 무시 */ }
            _buffer?.ClearBuffer();
            _aligner.Reset();
            _state = AudioDeviceState.ReleaseWait;
            _releaseAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ReleaseHoldMs;
        }
    }

    private void ExpireReleaseWait()
    {
        if (_state != AudioDeviceState.ReleaseWait) return;
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _releaseAtUnixMs) return;

        TearDown();
        _state = AudioDeviceState.Idle;
    }

    public void SetHardwareVolume(int percent)
    {
        // ASIO: 앱 디지털 감쇠로만 처리한다.
    }

    private void TearDown()
    {
        var driver = _asio;
        _asio = null;
        _buffer = null;
        _releaseAtUnixMs = 0;
        if (driver is null) return;

        try
        {
            _sta.Invoke(() =>
            {
                try { driver.Stop(); } catch { /* 무시 */ }
                driver.Dispose();
            });
        }
        catch
        {
            // 드라이버가 얼어 있으면 프로세스 종료로 정리된다 — 워커가 분리돼 있는 이유다.
        }
    }

    private bool SameFormat(DeviceConfig c) => SameFormat(c.SampleRate, c.BitDepth, c.Channels);

    private bool SameFormat(int rate, int bits, int channels)
        => _buffer is not null
           && _buffer.WaveFormat.SampleRate == rate
           && _buffer.WaveFormat.Channels == channels
           && _sourceBitDepth == (bits >= 24 ? 24 : 16);

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
        lock (_gate) TearDown();
        _sta.Dispose();
    }
}

/// <summary>
/// ASIO 드라이버 COM 개체를 만들고 쓰는 전용 STA 스레드.
///
/// 드라이버를 만든 아파트먼트와 다른 스레드에서 호출하면 드라이버 내부 상태가 어긋난다.
/// 만든 스레드가 먼저 끝나도 아파트먼트가 사라져 개체가 죽는다. 그래서 이 스레드가
/// 개체의 생성·초기화·재생·정지·해제를 모두 맡고, 수명 동안 살아 있는다.
/// </summary>
internal sealed class AsioStaHost : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;

    public string? Failure { get; private set; }

    public AsioStaHost()
    {
        _thread = new Thread(() =>
        {
            foreach (var job in _work.GetConsumingEnumerable())
            {
                job();
            }
        })
        {
            IsBackground = true,
            Name = "mono-asio-sta"
        };

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _thread.SetApartmentState(ApartmentState.STA);
            }
            catch (Exception ex)
            {
                Failure = "STA 스레드를 만들지 못했습니다 — " + ex.Message;
            }
        }

        _thread.Start();
    }

    /// <summary>STA 스레드에서 실행하고 끝날 때까지 기다린다. 예외는 호출자에게 그대로 올린다.</summary>
    public void Invoke(Action action)
        => Invoke<object?>(() =>
        {
            action();
            return null;
        });

    public T Invoke<T>(Func<T> func)
    {
        using var done = new ManualResetEventSlim(false);
        Exception? failure = null;
        var result = default(T);

        _work.Add(() =>
        {
            try { result = func(); }
            catch (Exception ex) { failure = ex; }
            finally { done.Set(); }
        });

        if (!done.Wait(TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException("ASIO 드라이버가 15초 안에 응답하지 않았습니다.");
        }

        if (failure is not null) throw failure;
        return result!;
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join(2000);
        _work.Dispose();
    }
}
