namespace Mono.Output;

/// <summary>
/// 장치가 등록될 때 고정되는 동작 모드. 묵시적 전환은 없다.
/// 비트퍼펙트를 고른 장치가 조용히 공유 모드로 내려가는 일은 없어야 한다 —
/// 그렇게 내려가면 OS 믹서가 리샘플링을 끼워 넣고도 아무 흔적을 남기지 않는다.
/// </summary>
public enum DeviceMode
{
    /// <summary>DAC 클럭에 1:1. 커널 믹서 우회, 하드웨어 볼륨 고정.</summary>
    BitPerfectExclusive = 0,

    /// <summary>타 응용프로그램과 동시 출력을 허용하는 일반 감상 모드.</summary>
    SystemShared = 1
}

/// <summary>출력 장치 상태 머신.</summary>
public enum AudioDeviceState
{
    /// <summary>재생 명령 대기.</summary>
    Idle = 0,

    /// <summary>드라이버 핸들 점유 및 클럭 협상 중.</summary>
    Initializing = 1,

    /// <summary>비트퍼펙트 독점 스트리밍.</summary>
    ExclusiveStreaming = 2,

    /// <summary>공유 모드 스트리밍.</summary>
    SharedStreaming = 3,

    /// <summary>타 프로그램이 장치를 점유해 비트퍼펙트를 시작할 수 없다. 강등하지 않고 멈춘다.</summary>
    DeviceBusyLocked = 4,

    /// <summary>정지 직후 핸들을 잠시 유지하는 구간. 잦은 재시작 레이턴시를 막는다.</summary>
    ReleaseWait = 5,

    /// <summary>USB 탈착 등 물리적 연결 해제. 스트림을 멈추고 자원을 정리했다.</summary>
    DeviceLostSuspend = 6
}

/// <summary>장치를 열 때 요구하는 규격.</summary>
public readonly record struct DeviceConfig(
    int SampleRate,
    int BitDepth,
    int Channels,
    bool IsDsd,
    DeviceMode Mode)
{
    public int BlockAlign => Math.Max(1, Channels) * Math.Max(1, BitDepth / 8);
}

/// <summary>장치 큐에 기록할 오디오 프레임 한 덩어리.</summary>
public readonly record struct AudioBuffer(
    byte[] Data,
    int Offset,
    int Count,
    int SampleRate,
    int BitDepth,
    int Channels,
    bool IsDsd);

/// <summary>하드웨어가 실제로 받아 주는 규격.</summary>
public sealed record Capabilities(
    string DeviceName,
    IReadOnlyList<int> SampleRates,
    IReadOnlyList<int> BitDepths,
    int MaxChannels,
    bool SupportsExclusive,
    bool HardwareVolume)
{
    public static Capabilities Unknown(string name)
        => new(name, [44100, 48000], [16, 24], 2, false, false);
}

/// <summary>
/// 모든 출력 엔드포인트(WASAPI · ASIO · 추후 네트워크 브릿지)가 지키는 공통 규격.
/// </summary>
public interface IAudioOutputDevice : IDisposable
{
    string DeviceName { get; }
    bool HardwareVolume { get; }
    long LatencyMs { get; }

    /// <summary>재생을 시작하기 전에 채워 둘 버퍼 깊이(ms).</summary>
    int PrimeMs { get; set; }

    double BufferedMs { get; }

    /// <summary>장치 큐가 담을 수 있는 최대 깊이(ms). 이걸 넘겨 밀어 넣으면 조용히 버려진다.</summary>
    int CapacityMs { get; }
    bool Playing { get; }
    bool Exclusive { get; }

    /// <summary>점유 실패 등으로 재생을 보류했을 때 사용자에게 보여 줄 사유.</summary>
    string? LastError { get; }

    /// <summary>지정한 규격으로 드라이버를 초기화한다. 실패하면 상태에 사유가 남는다.</summary>
    bool OpenDevice(DeviceConfig config);

    /// <summary>실시간 오디오 프레임을 장치 큐에 기록한다.</summary>
    bool PushSamples(AudioBuffer buffer, int volumePercent);

    /// <summary>하드웨어가 물리적으로 지원하는 샘플레이트·비트뎁스 목록.</summary>
    Capabilities GetSupportedFormats();

    /// <summary>점유·스트리밍·언더런 등 실시간 상태.</summary>
    AudioDeviceState GetCurrentState();

    /// <summary>드라이버 핸들과 메모리를 해제하고 장치를 시스템에 반환한다.</summary>
    void ReleaseDevice();

    void Flush();
    void SetHardwareVolume(int percent);
}
