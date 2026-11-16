using Mono.Output;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 일시정지하면 장치 스트림이 실제로 멈춰야 한다.
///
/// NAudio 의 WasapiOut.Pause() 는 상태값만 바꾸고 오디오 클라이언트는 계속 돌린다. 공유 모드에서는
/// Windows 오디오 엔진이 빈 자리를 무음으로 채워 티가 안 나지만, 배타 모드에서는 장치가 마지막으로
/// 받은 버퍼를 되풀이해 내보낸다 — 일시정지할 때 들리던 "이상한 소리"다.
///
/// 실제 오디오 장치가 있어야 돈다(없으면 그냥 끝난다). 무음만 넣고 공유 모드로 열어, 소리도 나지 않고
/// 다른 프로그램의 소리도 막지 않는다.
/// </summary>
public class DevicePauseTests
{
    private static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < until)
        {
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    [Fact]
    public void FlushStopsTheDeviceStreamInsteadOfLeavingItRunningUnfed()
    {
        using var device = new WasapiOutputDevice(null, DeviceMode.SystemShared, allowSharedFallback: false);
        if (device.LastError is not null) return; // 오디오 장치가 없는 환경

        device.PrimeMs = 20;
        var silence = new byte[48000 / 5 * 2 * 2]; // 200ms, 16bit 스테레오, 전부 0
        if (!device.PushSamples(new AudioBuffer(silence, 0, silence.Length, 48000, 16, 2, false), 100)) return;
        if (!WaitFor(() => device.Playing, 1000)) return;

        // 재는 방법이 맞는지 먼저: 돌고 있는 스트림은 위치가 늘어난다.
        var start = device.StreamPositionBytes ?? 0;
        Assert.True(WaitFor(() => (device.StreamPositionBytes ?? 0) > start, 1000), "재생 중인데 위치가 늘지 않는다");

        device.Flush();
        Thread.Sleep(100);

        // 멈추고 비운 스트림은 위치가 0 으로 돌아간다(IAudioClient::Stop + Reset). 위치가 그대로 남아 있으면
        // 스트림은 멈추지 않았고 장치 버퍼에 마지막 음악 조각이 남아 있다 — 배타 모드에서 되풀이되는 그 조각이다.
        // 이 PC 의 드라이버처럼 위치만 멈추고 소리는 내지 않는 장치도 있어서, "위치가 안 늘어난다"로는 가를 수 없다.
        Assert.False(device.Playing);
        Assert.Equal(0, device.StreamPositionBytes ?? 0);
    }
}
