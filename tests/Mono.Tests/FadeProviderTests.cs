using Mono.Output;
using NAudio.Wave;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 끊고 잇는 자리의 페이드. 평소에는 바이트를 건드리지 않아야 하고, 끊을 때는 0 까지 매끄럽게
/// 내려가야 한다(그 자리에서 값이 뚝 떨어지면 딸깍 소리다).
/// </summary>
public class FadeProviderTests
{
    private const int Rate = 48000;

    /// <summary>모든 샘플이 같은 값인 끝없는 소스.</summary>
    private sealed class Constant(WaveFormat format, Action<byte[], int, int> fill) : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = format;

        public int Read(byte[] buffer, int offset, int count)
        {
            fill(buffer, offset, count);
            return count;
        }
    }

    private static FadeProvider FloatSource(float level, int channels = 2)
        => new(new Constant(
            WaveFormat.CreateIeeeFloatWaveFormat(Rate, channels),
            (buffer, offset, count) =>
            {
                for (var i = 0; i + 3 < count; i += 4) BitConverter.TryWriteBytes(buffer.AsSpan(offset + i), level);
            }));

    private static float[] ReadFloats(FadeProvider fade, int frames, int channels = 2)
    {
        var bytes = new byte[frames * channels * 4];
        Assert.Equal(bytes.Length, fade.Read(bytes, 0, bytes.Length));
        var floats = new float[frames * channels];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private static int RampFrames => Rate * FadeProvider.RampMs / 1000;

    [Fact]
    public void NormalPlaybackIsNotTouched()
    {
        var fade = FloatSource(0.123456789f);
        Assert.All(ReadFloats(fade, 4800), s => Assert.Equal(0.123456789f, s));
    }

    [Fact]
    public void FadeOutReachesSilenceWithoutAStep()
    {
        var fade = FloatSource(0.8f);
        ReadFloats(fade, 100);

        fade.BeginFadeOut();
        Assert.False(fade.WaitSilent(0));
        var ramp = ReadFloats(fade, RampFrames + 50);

        // 단조 감소하고, 한 프레임에 떨어지는 폭이 작다(올림 코사인의 가장 가파른 곳도 전체의 1% 미만).
        for (var f = 1; f < RampFrames; f++)
        {
            var drop = ramp[(f - 1) * 2] - ramp[f * 2];
            Assert.InRange(drop, -1e-6f, 0.8f * 0.01f);
        }

        // 좌우는 같은 프레임에서 같은 게인이다.
        for (var f = 0; f < RampFrames; f++) Assert.Equal(ramp[f * 2], ramp[f * 2 + 1]);

        Assert.InRange(ramp[0], 0.79f, 0.8f);
        Assert.All(ramp.Skip(RampFrames * 2), s => Assert.Equal(0f, s));
        Assert.True(fade.WaitSilent(0));
    }

    [Fact]
    public void FadeInStartsFromSilenceAndReturnsToUnity()
    {
        var fade = FloatSource(0.5f);
        fade.ArmFadeIn();

        var ramp = ReadFloats(fade, RampFrames + 100);

        Assert.InRange(ramp[0], 0f, 0.001f);
        for (var f = 1; f < RampFrames; f++) Assert.True(ramp[f * 2] >= ramp[(f - 1) * 2]);
        // 다 올라온 뒤로는 다시 손대지 않는다.
        Assert.All(ramp.Skip(RampFrames * 2), s => Assert.Equal(0.5f, s));
    }

    /// <summary>올라가는 도중에 끊으면 그 높이에서 내려간다. 처음부터 다시 내리면 값이 위로 튄다.</summary>
    [Fact]
    public void FadeOutDuringFadeInContinuesFromTheCurrentLevel()
    {
        var fade = FloatSource(1f, channels: 1);
        fade.ArmFadeIn();
        var rising = ReadFloats(fade, RampFrames / 2, 1);

        fade.BeginFadeOut();
        var falling = ReadFloats(fade, RampFrames, 1);

        Assert.InRange(falling[0], rising[^1] - 0.02f, rising[^1] + 0.02f);
        Assert.Equal(0f, falling[^1]);
    }

    [Fact]
    public void ResetReturnsToPassThrough()
    {
        var fade = FloatSource(0.5f);
        fade.BeginFadeOut();
        ReadFloats(fade, RampFrames + 10);

        fade.Reset();

        Assert.All(ReadFloats(fade, 100), s => Assert.Equal(0.5f, s));
    }

    /// <summary>WASAPI 가 여는 정수 규격(16bit, 그리고 24bit 를 담은 32bit 컨테이너)에서도 같다.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void IntegerFormatsFadeToZero(int containerBits)
    {
        WaveFormat format = containerBits == 16
            ? new WaveFormat(Rate, 16, 2)
            : new ExtensiblePcmWaveFormat(Rate, containerBits: 32, validBits: 24, 2);
        var bytesPerSample = containerBits / 8;
        var fade = new FadeProvider(new Constant(format, (buffer, offset, count) =>
        {
            for (var i = 0; i + bytesPerSample <= count; i += bytesPerSample)
            {
                if (bytesPerSample == 2) BitConverter.TryWriteBytes(buffer.AsSpan(offset + i), (short)20000);
                else BitConverter.TryWriteBytes(buffer.AsSpan(offset + i), 20000 << 16);
            }
        }));

        int First(byte[] b, int frame) => bytesPerSample == 2
            ? BitConverter.ToInt16(b, frame * 4)
            : BitConverter.ToInt32(b, frame * 8) >> 16;

        var untouched = new byte[100 * 2 * bytesPerSample];
        fade.Read(untouched, 0, untouched.Length);
        Assert.Equal(20000, First(untouched, 50));

        fade.BeginFadeOut();
        var ramp = new byte[(RampFrames + 20) * 2 * bytesPerSample];
        fade.Read(ramp, 0, ramp.Length);

        Assert.InRange(First(ramp, 0), 19900, 20000);
        Assert.InRange(First(ramp, RampFrames / 2), 9000, 11000);
        Assert.Equal(0, First(ramp, RampFrames + 5));
    }
}
