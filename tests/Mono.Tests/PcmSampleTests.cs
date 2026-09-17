using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 뎁스를 접을 때 파형이 살아남는가. 크기만 맞고 값이 틀리면 소리는 여전히 잡음이다.
/// </summary>
public class PcmSampleTests
{
    [Theory]
    [InlineData(8, 16)]
    [InlineData(16, 16)]
    [InlineData(24, 24)]
    [InlineData(32, 24)]
    public void DeviceDepthFoldsToWhatTheOutputLayerCanActuallyOpen(int source, int expected)
        => Assert.Equal(expected, PcmSamples.DeviceDepth(source));

    /// <summary>16/24bit 정수는 비트퍼펙트 경로다 — 배열이 그대로 나와야 한다.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    public void NativeDepthsPassThroughUntouched(int bits)
    {
        var pcm = new byte[120];
        Random.Shared.NextBytes(pcm);

        var got = PcmSamples.ToDeviceDepth(pcm, bits, PcmEncoding.Integer, out var deviceBits);

        Assert.Same(pcm, got);
        Assert.Equal(bits, deviceBits);
    }

    /// <summary>32bit float 사인파가 24bit 사인파로 그대로 남는가.</summary>
    [Fact]
    public void FloatSamplesLandOnTheSameWaveformIn24Bit()
    {
        const int n = 512;
        var src = new byte[n * 4];
        var expected = new double[n];
        for (var i = 0; i < n; i++)
        {
            var s = Math.Sin(2 * Math.PI * i / 64) * 0.75;
            expected[i] = s;
            BitConverter.TryWriteBytes(src.AsSpan(i * 4), (float)s);
        }

        var got = PcmSamples.ToDeviceDepth(src, 32, PcmEncoding.Float, out var bits);

        Assert.Equal(24, bits);
        Assert.Equal(n * 3, got.Length);
        for (var i = 0; i < n; i++)
        {
            var v = got[i * 3] | (got[i * 3 + 1] << 8) | (got[i * 3 + 2] << 16);
            if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
            Assert.True(Math.Abs(v / 8388607.0 - expected[i]) < 1e-5,
                $"샘플 {i}: {v / 8388607.0:F6} vs {expected[i]:F6}");
        }
    }

    /// <summary>32bit 정수는 상위 24비트가 그대로 남아야 한다.</summary>
    [Fact]
    public void Integer32KeepsItsTop24Bits()
    {
        var src = new byte[12];
        BitConverter.TryWriteBytes(src.AsSpan(0), 0x12345678);
        BitConverter.TryWriteBytes(src.AsSpan(4), unchecked((int)0xFEDCBA98));
        BitConverter.TryWriteBytes(src.AsSpan(8), 0);

        var got = PcmSamples.ToDeviceDepth(src, 32, PcmEncoding.Integer, out var bits);

        Assert.Equal(24, bits);
        Assert.Equal([0x56, 0x34, 0x12, 0xBA, 0xDC, 0xFE, 0x00, 0x00, 0x00], got);
    }

    /// <summary>클리핑은 값을 감싸지 않고 잘라야 한다 — 감싸면 최대음에서 폭발음이 난다.</summary>
    [Fact]
    public void OutOfRangeFloatsClipInsteadOfWrapping()
    {
        var src = new byte[8];
        BitConverter.TryWriteBytes(src.AsSpan(0), 4.0f);
        BitConverter.TryWriteBytes(src.AsSpan(4), -4.0f);

        var got = PcmSamples.ToDeviceDepth(src, 32, PcmEncoding.Float, out _);

        Assert.Equal([0xFF, 0xFF, 0x7F, 0x00, 0x00, 0x80], got);
    }

    /// <summary>8bit WAV 은 부호 없는 0~255 다. 그대로 부호 있는 값으로 읽으면 DC 가 얹힌다.</summary>
    [Fact]
    public void EightBitIsTreatedAsUnsigned()
    {
        var got = PcmSamples.ToDeviceDepth([128, 255, 0], 8, PcmEncoding.Integer, out var bits);

        Assert.Equal(16, bits);
        Assert.Equal(0, BitConverter.ToInt16(got, 0));
        Assert.True(BitConverter.ToInt16(got, 2) > 32000);
        Assert.True(BitConverter.ToInt16(got, 4) < -32000);
    }
}
