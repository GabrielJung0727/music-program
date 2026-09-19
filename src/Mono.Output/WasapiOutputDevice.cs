using System.Runtime.InteropServices;
using Mono.Shared;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Mono.Output;

/// <summary>
/// WASAPI 출력 엔드포인트.
///
/// 배타 모드가 거절되면 공유 모드로 내려가되, 조용히 내려가지는 않는다.
/// 조용한 강등이 나쁜 이유는 그대로다 — OS 믹서가 리샘플링을 끼워 넣고도 아무 흔적을
/// 남기지 않아 사용자는 비트퍼펙트라고 믿으면서 변형된 소리를 듣게 된다. 그래서
/// <see cref="Exclusive"/> 를 내리고 상태를 SharedStreaming 으로 바꾸고 사유를
/// <see cref="LastError"/> 에 남긴다. 표시는 정직하게, 소리는 끊기지 않게.
///
/// 강등 자체를 거부해야 하는 자리(측정용·오디오파일 룸)는 strictExclusive 로 옛 동작을
/// 그대로 쓴다 — 그때는 DeviceBusyLocked 로 멈추고 사유만 남긴다.
/// </summary>
public sealed class WasapiOutputDevice : IAudioOutputDevice
{
    /// <summary>AUDCLNT_E_DEVICE_IN_USE — 타 프로그램이 배타 점유 중.</summary>
    private const int DeviceInUse = unchecked((int)0x8889000A);

    /// <summary>정지 후 핸들을 붙들고 있는 시간. 잦은 재시작 레이턴시를 막는다.</summary>
    private const int ReleaseHoldMs = 3000;

    /// <summary>장치 큐가 담을 수 있는 최대 깊이.</summary>
    public int CapacityMs => 400;

    private static readonly int[] ProbeRates = [44100, 48000, 88200, 96000, 176400, 192000, 352800, 384000];

    /// <summary>
    /// 이 계층이 실제로 여는 뎁스는 16 과 24 둘뿐이다(<see cref="BuildFormat"/>).
    /// 여기에 32 를 넣으면 24 로 접혀 열리고도 "32bit 지원"으로 보고돼, Core 가 32bit 트랙을
    /// 네이티브로 보낼 수 있다고 믿는다. 그러면 4바이트 샘플이 3바이트 스트림으로 들어간다.
    /// </summary>
    private static readonly int[] ProbeDepths = [16, 24];

    private readonly object _gate = new();
    private readonly DeviceMode _mode;
    private readonly bool _allowSharedFallback;
    private readonly MMDeviceEnumerator? _enumerator;
    private readonly LostWatcher? _watcher;
    private readonly MMDevice? _device;
    private readonly string? _deviceId;

    private WasapiOut? _out;
    private BufferedWaveProvider? _buffer;
    private AudioDeviceState _state = AudioDeviceState.Idle;
    private long _releaseAtUnixMs;
    private readonly FrameAligner _aligner = new();

    /// <summary>지금 열려 있는 논리 규격(16/24bit 기준). 장치 규격은 24bit 를 32bit 컨테이너로 담는다.</summary>
    private DeviceConfig? _openConfig;

    public WasapiOutputDevice(string? deviceHint, DeviceMode mode, bool allowSharedFallback = true)
    {
        _mode = mode;
        _allowSharedFallback = allowSharedFallback && mode == DeviceMode.BitPerfectExclusive;
        DeviceName = "default";
        try
        {
            _enumerator = new MMDeviceEnumerator();
            var devices = _enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active)
                .ToList();
            _device = deviceHint is null
                ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : devices.FirstOrDefault(d => d.FriendlyName.Contains(deviceHint, StringComparison.OrdinalIgnoreCase))
                  ?? _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            DeviceName = _device.FriendlyName;
            _deviceId = _device.ID;
            HardwareVolume = true;

            // USB 케이블을 뽑으면 스트림을 즉시 멈춰야 한다. 안 그러면 죽은 핸들에 계속 쓴다.
            _watcher = new LostWatcher(this);
            _enumerator.RegisterEndpointNotificationCallback(_watcher);
        }
        catch (Exception ex)
        {
            HardwareVolume = false;
            LastError = ex.Message;
        }
    }

    public string DeviceName { get; }
    public bool HardwareVolume { get; }
    public long LatencyMs { get; private set; } = 10;
    public int PrimeMs { get; set; } = 50;
    public bool Exclusive { get; private set; }
    public string? LastError { get; private set; }

    public bool Playing => _out?.PlaybackState == PlaybackState.Playing;

    public double BufferedMs
    {
        get { lock (_gate) return _buffer?.BufferedDuration.TotalMilliseconds ?? 0; }
    }

    /// <summary>목표 버퍼 대비 실제 버퍼의 어긋남. 락 판정에 쓴다.</summary>
    public double DriftMs { get; private set; }

    public AudioDeviceState GetCurrentState()
    {
        lock (_gate)
        {
            ExpireReleaseWait();
            return _state;
        }
    }

    private Capabilities? _caps;

    public Capabilities GetSupportedFormats()
    {
        if (_caps is not null) return _caps;
        if (_device is null) return _caps = Capabilities.Unknown(DeviceName);

        var share = _mode == DeviceMode.BitPerfectExclusive
            ? AudioClientShareMode.Exclusive
            : AudioClientShareMode.Shared;

        var rates = new List<int>();
        var depths = new List<int>();
        var supportsExclusive = false;

        foreach (var rate in ProbeRates)
        {
            foreach (var depth in ProbeDepths)
            {
                // 폴백이 열려 있으면 "공유로만 되는 규격"도 실제로 낼 수 있는 규격이다.
                // 배타 목록만 보고하면 Core 는 낼 수 있는 트랙을 못 낸다고 판단한다.
                var playable = Probe(rate, depth, 2, share)
                               || (_allowSharedFallback && Probe(rate, depth, 2, AudioClientShareMode.Shared));
                if (!playable) continue;
                if (!rates.Contains(rate)) rates.Add(rate);
                if (!depths.Contains(depth)) depths.Add(depth);
            }

            if (!supportsExclusive && Probe(rate, 24, 2, AudioClientShareMode.Exclusive))
            {
                supportsExclusive = true;
            }
        }

        if (rates.Count == 0)
        {
            // 어떤 후보도 못 열면 최소한 믹스 포맷은 알려 준다.
            try
            {
                var mix = _device.AudioClient.MixFormat;
                rates.Add(mix.SampleRate);
                depths.Add(mix.BitsPerSample);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        depths.Sort();
        return _caps = new Capabilities(DeviceName, rates, depths, 2, supportsExclusive, HardwareVolume);
    }

    private bool Probe(int rate, int depth, int channels, AudioClientShareMode share)
    {
        try
        {
            return _device!.AudioClient.IsFormatSupported(share, BuildFormat(rate, depth, channels));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool OpenDevice(DeviceConfig config)
    {
        lock (_gate)
        {
            // 같은 규격으로 다시 열기 — RELEASE WAIT 중이면 핸들을 그대로 이어 쓴다.
            if (_state == AudioDeviceState.ReleaseWait && _out is not null && SameFormat(config))
            {
                _state = Exclusive ? AudioDeviceState.ExclusiveStreaming : AudioDeviceState.SharedStreaming;
                _releaseAtUnixMs = 0;
                return true;
            }

            if (_state == AudioDeviceState.DeviceLostSuspend) return false;

            _state = AudioDeviceState.Initializing;
            LastError = null;
            TearDown();
            _aligner.Reset();

            var preferred = _mode == DeviceMode.BitPerfectExclusive
                ? AudioClientShareMode.Exclusive
                : AudioClientShareMode.Shared;

            if (TryOpen(config, preferred, out var refusal))
            {
                return true;
            }

            // 배타를 거절하는 장치에서 소리를 통째로 잃지 않는다. 대신 비트퍼펙트라고 말하지 않는다.
            if (_allowSharedFallback
                && preferred == AudioClientShareMode.Exclusive
                && TryOpen(config, AudioClientShareMode.Shared, out _))
            {
                LastError = "이 장치가 비트퍼펙트(배타) 재생을 받지 않아 WASAPI 공유 모드로 재생합니다 — "
                          + "OS 믹서를 거치므로 비트퍼펙트가 아닙니다. 사유: " + refusal;
                Console.WriteLine("device fallback: 배타 → 공유 · " + refusal);
                return true;
            }

            _state = AudioDeviceState.DeviceBusyLocked;
            LastError = refusal;
            return false;
        }
    }

    /// <summary>한 공유 모드로 장치를 열어 본다. 성공하면 핸들과 상태를 여기서 확정한다.</summary>
    private bool TryOpenOnce(DeviceConfig config, AudioClientShareMode share, out string refusal)
    {
        var latency = share == AudioClientShareMode.Exclusive ? 10 : 20;
        var requested = BuildFormat(config.SampleRate, config.BitDepth, config.Channels);
        var buffer = new BufferedWaveProvider(requested)
        {
            // overflow 시 샘플을 버리면 파형 불연속(지직)이 난다. 상위에서 백프레셔로 막는다.
            DiscardOnBufferOverflow = false,
            BufferDuration = TimeSpan.FromMilliseconds(CapacityMs)
        };

        WasapiOut? attempt = null;
        try
        {
            attempt = _device is null
                ? new WasapiOut(share, latency)
                : new WasapiOut(_device, share, true, latency);
            attempt.Init(buffer);

            // NAudio 는 요청 포맷을 장치가 못 받으면 예외를 던지지 않는다. 배타 모드에서도
            // 지원되는 포맷으로 바꿔 잡고 DMO 리샘플러를 조용히 끼워 넣는다. 그러면 상태는
            // ExclusiveStreaming 인데 실제로는 비트퍼펙트가 아니다 — 우리가 막으려던 바로 그
            // 강등이 라이브러리 안쪽에서 일어난다. 하드웨어가 실제로 쓰는 포맷을 직접 확인한다.
            var actual = attempt.OutputWaveFormat;
            Console.WriteLine(
                $"device open: 요청 {config.SampleRate}/{config.BitDepth}/{config.Channels} → " +
                $"실제 {actual.SampleRate}/{actual.BitsPerSample}/{actual.Channels} ({share})");

            if (share == AudioClientShareMode.Exclusive && !Matches(actual, requested))
            {
                refusal = DescribeFormatMismatch(config, actual);
                try { attempt.Dispose(); } catch { /* 이미 닫혔으면 무시 */ }
                return false;
            }

            _out = attempt;
            _buffer = buffer;
            _openConfig = config;
            Exclusive = share == AudioClientShareMode.Exclusive;
            LatencyMs = latency;
            _state = Exclusive ? AudioDeviceState.ExclusiveStreaming : AudioDeviceState.SharedStreaming;
            refusal = "";
            return true;
        }
        catch (Exception ex)
        {
            try { attempt?.Dispose(); } catch { /* 이미 닫혔으면 무시 */ }
            _out = null;
            _buffer = null;
            _openConfig = null;
            refusal = Describe(ex, config);
            return false;
        }
    }

    /// <summary>
    /// 장치가 잠겨 있으면 잠깐 기다렸다가 다시 연다.
    /// ASIO 워커를 막 죽인 직후 WASAPI 가 같은 Fireface 를 열 때 흔하다.
    /// </summary>
    private bool TryOpen(DeviceConfig config, AudioClientShareMode share, out string refusal)
    {
        refusal = "";
        // ASIO 드라이버가 장치를 놓는 데 걸리는 시간은 장치마다 다르다. Fireface 처럼
        // 끈질긴 장치는 2초를 넘기기도 해서, 예전 창(4회 × 600ms)에서는 간헐적으로
        // 열리지 않고 그대로 실패했다. 기다리는 쪽이 실패보다 낫다 — 바쁘다는 응답일 때만 돈다.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (TryOpenOnce(config, share, out refusal)) return true;
            if (!LooksBusy(refusal)) return false;
            Thread.Sleep(600);
        }

        return false;
    }

    private static bool LooksBusy(string refusal)
        => refusal.Contains("0x8889000A", StringComparison.OrdinalIgnoreCase)
           || refusal.Contains("0x88890008", StringComparison.OrdinalIgnoreCase)
           || refusal.Contains("being used", StringComparison.OrdinalIgnoreCase)
           || refusal.Contains("사용 중", StringComparison.OrdinalIgnoreCase)
           || refusal.Contains("in use", StringComparison.OrdinalIgnoreCase)
           || refusal.Contains("AUDCLNT_E_DEVICE_IN_USE", StringComparison.OrdinalIgnoreCase)
           || refusal.Contains("AUDCLNT_E_DEVICE_INVALIDATED", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(WaveFormat actual, WaveFormat requested)
        => actual.SampleRate == requested.SampleRate
           && actual.BitsPerSample == requested.BitsPerSample
           && actual.Channels == requested.Channels;

    private string DescribeFormatMismatch(DeviceConfig config, WaveFormat actual)
    {
        var caps = GetSupportedFormats();
        var rates = caps.SampleRates.Count > 0 ? string.Join(", ", caps.SampleRates) + "Hz" : "알 수 없음";
        return $"이 장치는 {config.SampleRate}Hz/{config.BitDepth}bit 를 배타 모드로 받지 않습니다 "
             + $"(드라이버가 {actual.SampleRate}Hz/{actual.BitsPerSample}bit 로 바꿔 리샘플링하려 합니다). "
             + $"장치가 받는 규격: {rates}. "
             + "비트퍼펙트를 지키려면 인터페이스 설정에서 샘플레이트를 트랙에 맞추거나 출력 드라이버를 ASIO 로 바꾸세요.";
    }

    private static string Describe(Exception ex, DeviceConfig config)
    {
        if (ex is COMException com && com.HResult == DeviceInUse)
        {
            return "다른 응용프로그램이 이 장치를 점유하고 있어 비트퍼펙트 재생을 시작할 수 없습니다.";
        }

        return $"{config.SampleRate}Hz/{config.BitDepth}bit/{config.Channels}ch 로 장치를 열지 못했습니다 — {ex.Message}";
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
                if (!OpenDevice(new DeviceConfig(buffer.SampleRate, bits, channels, buffer.IsDsd, _mode)))
                {
                    return false;
                }
            }

            var payload = new ReadOnlySpan<byte>(buffer.Data, buffer.Offset, buffer.Count).ToArray();

            // 마지막 방어선. 여기 닿는 페이로드는 이미 16/24 여야 하지만, 뎁스만 접히고 바이트는
            // 안 접힌 채 들어오면 장치가 바이트열을 잘못된 보폭으로 읽어 전 구간이 잡음이 된다.
            // 소리를 망가뜨린 채 내보내느니 여기서 맞춰 넣는다.
            if (buffer.BitDepth != bits && !buffer.IsDsd)
            {
                Console.WriteLine($"push: {buffer.BitDepth}bit 페이로드를 {bits}bit 로 변환합니다 — 상위 디코더가 뎁스를 접지 않았습니다.");
                payload = PcmSamples.ToDeviceDepth(payload, buffer.BitDepth, PcmEncoding.Integer, out _);
            }

            if (!HardwareVolume && volumePercent < 100)
            {
                payload = Attenuate(payload, bits, volumePercent);
            }

            // 온전한 프레임만 넘긴다. 남는 꼬리는 다음 청크 앞에 붙는다.
            // 정렬은 들어온 규격(16/24bit) 기준이다 — 컨테이너로 넓히는 건 그 다음이다.
            var aligned = _aligner.Take(payload, channels * (bits / 8));
            if (aligned.Length == 0) return true;

            // 24bit 는 32bit 컨테이너에 상위 정렬로 담아 넘긴다. 장치 규격이 그렇게 열려 있다.
            var frames = _buffer!.WaveFormat.BitsPerSample == 32 && bits == 24
                ? Widen24To32(aligned)
                : aligned;

            try
            {
                _buffer.AddSamples(frames, 0, frames.Length);
            }
            catch (InvalidOperationException)
            {
                // 버퍼가 가득 참 — 이번 청크는 건너뛰고 다음 루프에서 다시 맞춘다.
                return true;
            }

            // 빈 버퍼로 재생을 시작하면 첫 순간부터 언더런이다. 목표 깊이를 채운 뒤에 연다.
            if (_out is not null
                && _out.PlaybackState != PlaybackState.Playing
                && _buffer.BufferedDuration.TotalMilliseconds >= PrimeMs)
            {
                _out.Play();
            }

            DriftMs = _buffer.BufferedDuration.TotalMilliseconds - LatencyMs;
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
            try { _out?.Pause(); } catch { /* 장치가 이미 닫혔으면 무시 */ }
        }
    }

    /// <summary>
    /// 핸들을 즉시 끊지 않고 3초간 유지한다. 곡을 넘길 때마다 드라이버를 다시 여는 비용을 피한다.
    /// </summary>
    public void ReleaseDevice()
    {
        lock (_gate)
        {
            if (_out is null)
            {
                _state = AudioDeviceState.Idle;
                return;
            }

            try { _out.Pause(); } catch { /* 무시 */ }
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
        if (_device is null) return;
        try
        {
            _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f;
        }
        catch (Exception)
        {
            // 하드웨어 볼륨을 못 쓰면 PushSamples 에서 디지털 감쇠로 처리한다.
        }
    }

    /// <summary>USB 탈착 등으로 장치가 사라졌다. 스트림을 멈추고 자원을 정리한다.</summary>
    internal void OnDeviceLost()
    {
        lock (_gate)
        {
            TearDown();
            _aligner.Reset();
            _state = AudioDeviceState.DeviceLostSuspend;
            LastError = "출력 장치의 연결이 끊어졌습니다. 케이블을 다시 연결한 뒤 재생하십시오.";
        }
    }

    /// <summary>장치가 돌아왔다. 다음 재생에서 다시 열도록 대기 상태로 되돌린다.</summary>
    internal void OnDeviceBack()
    {
        lock (_gate)
        {
            if (_state != AudioDeviceState.DeviceLostSuspend) return;
            _state = AudioDeviceState.Idle;
            LastError = null;
        }
    }

    internal bool IsThisDevice(string? id) => id is not null && id == _deviceId;

    private void TearDown()
    {
        try { _out?.Stop(); } catch { /* 무시 */ }
        try { _out?.Dispose(); } catch { /* 무시 */ }
        _out = null;
        _buffer = null;
        _openConfig = null;
        _releaseAtUnixMs = 0;
    }

    private bool SameFormat(DeviceConfig c) => SameFormat(c.SampleRate, c.BitDepth, c.Channels);

    /// <summary>
    /// 지금 열린 장치가 이 논리 규격을 담고 있는가.
    ///
    /// 장치 쪽 WaveFormat 을 보면 안 된다 — 24bit 는 32bit 컨테이너로 열리므로
    /// BitsPerSample 이 32 로 잡혀 매번 "다른 규격"이 되고, 청크마다 장치를 다시 연다.
    /// </summary>
    private bool SameFormat(int rate, int bits, int channels)
        => _buffer is not null
           && _openConfig is { } open
           && open.SampleRate == rate
           && open.BitDepth == bits
           && open.Channels == channels;

    /// <summary>
    /// 논리 규격(16/24bit)을 장치에 넘길 WaveFormat 으로 바꾼다.
    ///
    /// 24bit 를 WAVEFORMATEX 로 요청하면 3바이트 패킹인지 4바이트 컨테이너의 상위 24비트인지가
    /// 정해지지 않는다. 드라이버는 그 모호한 요청에도 "지원한다"고 답해 놓고 배타 모드에서
    /// 4바이트씩 읽어 갈 수 있다 — 그러면 첫 샘플부터 경계가 밀려 음악 전체가 잡음이 된다.
    /// 그래서 24bit 는 항상 컨테이너를 명시한 WAVEFORMATEXTENSIBLE 로 연다.
    /// </summary>
    public static WaveFormat BuildFormat(int rate, int bits, int channels)
        => bits >= 24
            ? new ExtensiblePcmWaveFormat(rate, containerBits: 32, validBits: 24, channels)
            : new WaveFormat(rate, 16, channels);

    /// <summary>24bit 패킹 PCM → 32bit 컨테이너 상위 정렬. 하위 바이트는 0 으로 채운다.</summary>
    public static byte[] Widen24To32(byte[] packed)
    {
        var samples = packed.Length / 3;
        var wide = new byte[samples * 4];
        for (var i = 0; i < samples; i++)
        {
            var s = i * 3;
            var d = i * 4;
            wide[d] = 0;
            wide[d + 1] = packed[s];
            wide[d + 2] = packed[s + 1];
            wide[d + 3] = packed[s + 2];
        }

        return wide;
    }

    private static byte[] Attenuate(byte[] pcm, int bits, int percent)
    {
        var gain = Math.Clamp(percent, 0, 100) / 100f;
        var copy = new byte[pcm.Length];
        Buffer.BlockCopy(pcm, 0, copy, 0, pcm.Length);
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
            var v = copy[i] | (copy[i + 1] << 8) | (copy[i + 2] << 16);
            if ((v & 0x800000) != 0)
            {
                v |= unchecked((int)0xFF000000);
            }

            v = (int)(v * gain);
            copy[i] = (byte)v;
            copy[i + 1] = (byte)(v >> 8);
            copy[i + 2] = (byte)(v >> 16);
        }

        return copy;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            TearDown();
        }

        try
        {
            if (_watcher is not null) _enumerator?.UnregisterEndpointNotificationCallback(_watcher);
        }
        catch
        {
            // 이미 해제됐으면 무시
        }

        _device?.Dispose();
        _enumerator?.Dispose();
    }

    /// <summary>엔드포인트 추가·제거·상태 변화 알림. 장치 분리를 즉시 잡기 위한 것.</summary>
    private sealed class LostWatcher(WasapiOutputDevice owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, NAudio.CoreAudioApi.DeviceState newState)
        {
            if (!owner.IsThisDevice(deviceId)) return;
            if (newState == NAudio.CoreAudioApi.DeviceState.Active) owner.OnDeviceBack();
            else owner.OnDeviceLost();
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            if (owner.IsThisDevice(pwstrDeviceId)) owner.OnDeviceBack();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            if (owner.IsThisDevice(deviceId)) owner.OnDeviceLost();
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }
    }
}
