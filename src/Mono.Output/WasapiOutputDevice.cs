using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Mono.Output;

/// <summary>
/// WASAPI 출력 엔드포인트.
///
/// 등록된 모드를 그대로 지킨다 — 비트퍼펙트로 열기로 한 장치가 점유에 실패했다고
/// 공유 모드로 조용히 내려가지 않는다. 내려가면 OS 믹서가 리샘플링을 끼워 넣고도
/// 아무 흔적을 남기지 않아, 사용자는 비트퍼펙트라고 믿으면서 변형된 소리를 듣는다.
/// 대신 DeviceBusyLocked 로 멈추고 사유를 남긴다.
/// </summary>
public sealed class WasapiOutputDevice : IAudioOutputDevice
{
    /// <summary>AUDCLNT_E_DEVICE_IN_USE — 타 프로그램이 배타 점유 중.</summary>
    private const int DeviceInUse = unchecked((int)0x8889000A);

    /// <summary>정지 후 핸들을 붙들고 있는 시간. 잦은 재시작 레이턴시를 막는다.</summary>
    private const int ReleaseHoldMs = 3000;

    private static readonly int[] ProbeRates = [44100, 48000, 88200, 96000, 176400, 192000, 352800, 384000];
    private static readonly int[] ProbeDepths = [16, 24, 32];

    private readonly object _gate = new();
    private readonly DeviceMode _mode;
    private readonly MMDeviceEnumerator? _enumerator;
    private readonly LostWatcher? _watcher;
    private readonly MMDevice? _device;
    private readonly string? _deviceId;

    private WasapiOut? _out;
    private BufferedWaveProvider? _buffer;
    private AudioDeviceState _state = AudioDeviceState.Idle;
    private long _releaseAtUnixMs;
    private readonly FrameAligner _aligner = new();

    public WasapiOutputDevice(string? deviceHint, DeviceMode mode)
    {
        _mode = mode;
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

    public Capabilities GetSupportedFormats()
    {
        if (_device is null) return Capabilities.Unknown(DeviceName);

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
                if (!Probe(rate, depth, 2, share)) continue;
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
        return new Capabilities(DeviceName, rates, depths, 2, supportsExclusive, HardwareVolume);
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
            _buffer = new BufferedWaveProvider(BuildFormat(config.SampleRate, config.BitDepth, config.Channels))
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(400)
            };

            var share = _mode == DeviceMode.BitPerfectExclusive
                ? AudioClientShareMode.Exclusive
                : AudioClientShareMode.Shared;
            var latency = share == AudioClientShareMode.Exclusive ? 10 : 20;

            WasapiOut? attempt = null;
            try
            {
                attempt = _device is null
                    ? new WasapiOut(share, latency)
                    : new WasapiOut(_device, share, true, latency);
                attempt.Init(_buffer);
                _out = attempt;
                Exclusive = share == AudioClientShareMode.Exclusive;
                LatencyMs = latency;
                _state = Exclusive ? AudioDeviceState.ExclusiveStreaming : AudioDeviceState.SharedStreaming;
                return true;
            }
            catch (Exception ex)
            {
                // 실패한 클라이언트를 놓아주지 않으면 장치를 문 채 남는다.
                try { attempt?.Dispose(); } catch { /* 이미 닫혔으면 무시 */ }
                _out = null;
                _buffer = null;

                // 자동 강등은 하지 않는다. 사유를 남기고 멈춘다.
                _state = AudioDeviceState.DeviceBusyLocked;
                LastError = Describe(ex, config);
                return false;
            }
        }
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

            var bits = buffer.BitDepth >= 24 ? 24 : 16;
            var channels = Math.Max(buffer.Channels, 1);
            if (_buffer is null || !SameFormat(buffer.SampleRate, bits, channels))
            {
                if (!OpenDevice(new DeviceConfig(buffer.SampleRate, bits, channels, buffer.IsDsd, _mode)))
                {
                    return false;
                }
            }

            var payload = new ReadOnlySpan<byte>(buffer.Data, buffer.Offset, buffer.Count).ToArray();
            if (!HardwareVolume && volumePercent < 100)
            {
                payload = Attenuate(payload, bits, volumePercent);
            }

            // 온전한 프레임만 넘긴다. 남는 꼬리는 다음 청크 앞에 붙는다.
            var aligned = _aligner.Take(payload, _buffer!.WaveFormat.BlockAlign);
            if (aligned.Length == 0) return true;

            _buffer.AddSamples(aligned, 0, aligned.Length);

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
        _releaseAtUnixMs = 0;
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
