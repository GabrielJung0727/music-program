using Mono.Output;
using Xunit;

namespace Mono.Tests;

/// <summary>오디오 HAL 계약 — 프레임 정렬과 상태 머신 값.</summary>
public class AudioDeviceTests
{
    /// <summary>
    /// 반 프레임이 장치 큐에 들어가면 그 뒤 모든 샘플이 밀려 좌우가 뒤바뀌고,
    /// 한 번 밀리면 스트림이 끝날 때까지 복구되지 않는다.
    /// </summary>
    [Fact]
    public void AlignerNeverEmitsAPartialFrame()
    {
        var aligner = new FrameAligner();
        const int blockAlign = 6;   // 24bit 스테레오

        var first = aligner.Take(new byte[3530], blockAlign);
        Assert.Equal(0, first.Length % blockAlign);
        Assert.Equal(2, aligner.Pending);
    }

    /// <summary>남은 꼬리는 버리지 않고 다음 청크 앞에 붙어 한 바이트도 잃지 않는다.</summary>
    [Fact]
    public void AlignerCarriesTheRemainderIntoTheNextChunk()
    {
        var aligner = new FrameAligner();
        const int blockAlign = 6;

        var total = 0;
        foreach (var size in new[] { 3530, 3526, 3528, 101 })
        {
            var got = aligner.Take(new byte[size], blockAlign);
            Assert.Equal(0, got.Length % blockAlign);
            total += got.Length;
        }

        // 내보낸 양 + 아직 들고 있는 양 = 넣은 양
        Assert.Equal(3530 + 3526 + 3528 + 101, total + aligner.Pending);
    }

    [Fact]
    public void AlignerPassesAlreadyAlignedChunksThrough()
    {
        var aligner = new FrameAligner();
        var got = aligner.Take(new byte[3528], 4);   // 16bit 스테레오
        Assert.Equal(3528, got.Length);
        Assert.Equal(0, aligner.Pending);
    }

    /// <summary>포맷이 바뀌면 이전 포맷의 꼬리를 새 포맷에 붙이면 안 된다.</summary>
    [Fact]
    public void ResetDropsTheCarryOnFormatChange()
    {
        var aligner = new FrameAligner();
        aligner.Take(new byte[7], 6);
        Assert.Equal(1, aligner.Pending);

        aligner.Reset();
        Assert.Equal(0, aligner.Pending);
    }

    /// <summary>
    /// Control 의 경보 판정이 상태 머신 값을 숫자로 비교한다(4=점유 잠김, 6=장치 분리).
    /// 열거형 순서가 바뀌면 경보가 엉뚱한 상태에 뜨므로 값을 고정한다.
    /// </summary>
    [Fact]
    public void BlockingStatesKeepTheirWireValues()
    {
        Assert.Equal(0, (int)AudioDeviceState.Idle);
        Assert.Equal(2, (int)AudioDeviceState.ExclusiveStreaming);
        Assert.Equal(4, (int)AudioDeviceState.DeviceBusyLocked);
        Assert.Equal(6, (int)AudioDeviceState.DeviceLostSuspend);
    }

    [Theory]
    [InlineData(16, 2, 4)]
    [InlineData(24, 2, 6)]
    [InlineData(24, 1, 3)]
    public void DeviceConfigReportsBlockAlign(int depth, int channels, int expected)
    {
        var config = new DeviceConfig(48000, depth, channels, false, DeviceMode.BitPerfectExclusive);
        Assert.Equal(expected, config.BlockAlign);
    }

    /// <summary>등록된 모드는 고정이다 — 비트퍼펙트가 공유 모드로 내려가면 안 된다.</summary>
    [Fact]
    public void BitPerfectIsTheDefaultAndSharedIsOptIn()
    {
        Assert.Equal(DeviceMode.BitPerfectExclusive, default(DeviceMode));
        Assert.NotEqual(DeviceMode.SystemShared, DeviceMode.BitPerfectExclusive);
    }
}
