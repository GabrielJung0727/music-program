using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Mono.Shared;
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

    /// <summary>
    /// 소리를 내보낼 첫 출력 채널(0부터). 스테레오는 여기서부터 두 채널을 쓴다.
    /// 출력이 여러 쌍인 인터페이스에서 헤드폰 단자(예: 3/4)를 고를 수 있게 한다.
    /// </summary>
    private readonly int _channelOffset;

    private AsioOut? _asio;
    private BufferedWaveProvider? _buffer;
    private FadeProvider? _fade;
    private bool _fadeInArmed;
    private AudioDeviceState _state = AudioDeviceState.Idle;
    private long _releaseAtUnixMs;
    private int _sourceBitDepth = 16;

    /// <summary>바로 앞 조각에 건 볼륨 게인. 다음 조각은 여기서부터 이어 올리거나 내린다.</summary>
    private float _gain = 1f;
    private bool _gainSet;

    private AsioAudioRenderer(AsioStaHost sta, string name) : this(sta, name, 0)
    {
    }

    private AsioAudioRenderer(AsioStaHost sta, string name, int channelOffset)
    {
        _sta = sta;
        _driverName = name;
        _channelOffset = Math.Max(0, channelOffset);
        DeviceName = _channelOffset == 0
            ? "ASIO · " + name
            : $"ASIO · {name} · 출력 {_channelOffset + 1}/{_channelOffset + 2}";
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

    public static AsioAudioRenderer? TryCreate(string? deviceHint, out string? error)
        => TryCreate(deviceHint, 0, out error);

    /// <summary>
    /// 드라이버의 출력 채널 이름. 드라이버를 잠깐 열어 물어본다 — 설정 화면이 고른 장치 하나에
    /// 대해서만 부른다. 열거할 때 모든 드라이버를 여는 건 위험하다(창을 띄우거나 멈추는 드라이버가 있다).
    /// </summary>
    public static string[] OutputChannelNames(string? deviceHint, out string? error)
    {
        error = null;
        var drivers = DriverNames();
        if (drivers.Length == 0)
        {
            error = "설치된 ASIO 드라이버가 없습니다.";
            return [];
        }

        var name = deviceHint is null
            ? drivers[0]
            : drivers.FirstOrDefault(d => d.Contains(deviceHint, StringComparison.OrdinalIgnoreCase)) ?? drivers[0];

        using var sta = new AsioStaHost();
        if (sta.Failure is not null)
        {
            error = sta.Failure;
            return [];
        }

        try
        {
            return sta.Invoke(() =>
            {
                using var driver = new AsioOut(name);
                var names = new string[driver.DriverOutputChannelCount];
                for (var i = 0; i < names.Length; i++)
                {
                    try { names[i] = driver.AsioOutputChannelName(i); }
                    catch { names[i] = $"Output {i + 1}"; }
                }

                return names;
            });
        }
        catch (Exception ex)
        {
            error = $"{name}: {ex.GetType().Name}: {ex.Message}";
            return [];
        }
    }

    /// <summary>
    /// ASIO 드라이버를 연다. 실패하면 사유를 돌려준다 — 사유를 삼키면 드라이버가 설치돼
    /// 있는데도 왜 안 열리는지 알 수 없다.
    /// </summary>
    public static AsioAudioRenderer? TryCreate(string? deviceHint, int channelOffset, out string? error)
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

        return new AsioAudioRenderer(sta, name, channelOffset);
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
    {
        int[] rates;
        try
        {
            rates = _sta.Invoke(() =>
            {
                var driver = _asio ?? new AsioOut(_driverName);
                try
                {
                    return new[] { 44100, 48000, 88200, 96000, 176400, 192000 }
                        .Where(rate => driver.IsSampleRateSupported(rate)).ToArray();
                }
                finally { if (!ReferenceEquals(driver, _asio)) driver.Dispose(); }
            });
        }
        catch (Exception ex)
        {
            LastError = $"ASIO 지원 샘플레이트 조회 실패: {ex.Message}";
            rates = [];
        }
        return new(DeviceName, rates, [16, 24], 2,
            SupportsExclusive: rates.Length > 0, HardwareVolume: false);
    }

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
                    DiscardOnBufferOverflow = false,
                    BufferDuration = TimeSpan.FromMilliseconds(CapacityMs)
                };

                // 드라이버는 버퍼가 아니라 페이드를 거쳐 읽는다. 평소에는 바이트를 그대로 넘긴다.
                var fade = new FadeProvider(_buffer);
                _fade = fade;
                _gainSet = false;
                _sta.Invoke(() =>
                {
                    var driver = new AsioOut(_driverName);
                    _asio = driver;
                    if (!driver.IsSampleRateSupported(config.SampleRate))
                        throw new NotSupportedException($"ASIO 드라이버가 {config.SampleRate}Hz를 지원하지 않습니다.");
                    if (_channelOffset + config.Channels > driver.DriverOutputChannelCount)
                        throw new NotSupportedException(
                            $"출력 {_channelOffset + 1}번부터 {config.Channels}채널을 쓸 수 없습니다 — 이 드라이버의 출력은 {driver.DriverOutputChannelCount}채널입니다.");
                    driver.ChannelOffset = _channelOffset;
                    driver.Init(fade);
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

            var bits = PcmSamples.DeviceDepth(buffer.BitDepth);
            var channels = Math.Max(buffer.Channels, 1);
            if (_buffer is null || !SameFormat(buffer.SampleRate, bits, channels))
            {
                var config = new DeviceConfig(
                    buffer.SampleRate, bits, channels, buffer.IsDsd, DeviceMode.BitPerfectExclusive);
                if (!OpenDevice(config)) return false;
            }

            var payload = new ReadOnlySpan<byte>(buffer.Data, buffer.Offset, buffer.Count).ToArray();

            // WASAPI 쪽과 같은 방어선 — 뎁스만 접히고 바이트는 안 접힌 페이로드를 그대로 쓰면
            // 이후 정렬이 통째로 밀려 전 구간이 잡음이 된다.
            if (buffer.BitDepth != bits && !buffer.IsDsd)
            {
                Console.WriteLine($"push: {buffer.BitDepth}bit 페이로드를 {bits}bit 로 변환합니다 — 상위 디코더가 뎁스를 접지 않았습니다.");
                payload = PcmSamples.ToDeviceDepth(payload, buffer.BitDepth, PcmEncoding.Integer, out _);
            }

            // 정렬은 원본 PCM 기준으로 잡는다 — 반 프레임을 float 로 바꾸면 어긋남이 굳어진다.
            var aligned = _aligner.Take(payload, channels * (bits / 8));
            if (aligned.Length == 0) return true;

            var floats = ToFloatBytes(aligned, bits);

            // 볼륨은 float 로 바꾼 뒤에 건다. 드라이버가 float 를 받으므로 정수로 되돌리는 단계가
            // 없고, 16bit 원본을 16bit 인 채로 줄여 하위 비트를 잃는 일도 없다. 게인은 앞 조각에서
            // 이번 조각까지 프레임 단위로 이어 준다 — 조각마다 계단처럼 바꾸면 지퍼 소리가 난다.
            // 100% 면 아무 계산도 하지 않는다.
            if (!buffer.IsDsd)
            {
                var gain = PcmGain.FromPercent(volumePercent);
                if (!_gainSet)
                {
                    // 재생을 막 시작한 조각은 처음부터 목표 볼륨이어야 한다.
                    _gain = gain;
                    _gainSet = true;
                }

                PcmGain.Apply(MemoryMarshal.Cast<byte, float>(floats.AsSpan()), channels, _gain, gain);
                _gain = gain;
            }

            try
            {
                _buffer!.AddSamples(floats, 0, floats.Length);
            }
            catch (InvalidOperationException)
            {
                return true;
            }

            // 빈 버퍼로 시작하면 첫 순간부터 언더런이다. 목표 깊이를 채운 뒤에 연다.
            if (_asio is not null
                && _asio.PlaybackState != PlaybackState.Playing
                && _buffer.BufferedDuration.TotalMilliseconds >= PrimeMs)
            {
                var driver = _asio;
                if (_fadeInArmed) _fade?.ArmFadeIn();
                else _fade?.Reset();
                _fadeInArmed = false;
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

    public void Flush() => Flush(fadeOut: false);

    public void FadeOutAndFlush() => Flush(fadeOut: true);

    public void ArmFadeIn(bool armed)
    {
        lock (_gate) _fadeInArmed = armed;
    }

    private void Flush(bool fadeOut)
    {
        lock (_gate)
        {
            if (_state == AudioDeviceState.DeviceBusyLocked)
            {
                TearDown();
                _state = AudioDeviceState.Idle;
                LastError = null;
            }

            // 드라이버가 페이드를 다 읽어 갈 때까지 잠깐 기다린다. 드라이버의 콜백은 이 잠금을
            // 쓰지 않으므로 기다리는 동안에도 계속 읽어 간다. 읽어 가지 않으면 시간만 채우고 넘어간다.
            if (fadeOut && _fade is { } fade && _buffer is { } queued
                && _asio?.PlaybackState == PlaybackState.Playing
                && queued.BufferedDuration.TotalMilliseconds > 0)
            {
                fade.BeginFadeOut();
                fade.WaitSilent(FadeProvider.RampMs * 8);
            }

            _buffer?.ClearBuffer();
            _aligner.Reset();
            _gainSet = false;
            // 비운 직후 그대로 재생하면 빈 버퍼를 긁는다. 다시 프라임될 때까지 멈춘다.
            var driver = _asio;
            if (driver is null) return;
            try { _sta.Invoke(() => driver.Pause()); }
            catch { /* 장치가 이미 닫혔으면 무시 */ }
            _fade?.Reset();
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
            _gainSet = false;
            _fade?.Reset();
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
        _fade = null;
        _gainSet = false;
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
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(() =>
        {
            try { done.TrySetResult(func()); }
            catch (Exception ex) { done.TrySetException(ex); }
        });
        // A timed-out call may finish later. Its completion must not touch a
        // disposed wait handle and crash the STA thread.
        return done.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        if (_thread.Join(2000)) _work.Dispose();
    }
}
