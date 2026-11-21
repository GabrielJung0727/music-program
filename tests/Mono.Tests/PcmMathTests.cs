using Mono.Output;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 정수 PCM 이 float 를 거쳐도 한 비트도 달라지지 않는가, 그리고 계산을 거친 신호를 되돌릴 때의 디더.
///
/// 예전에는 8388608 로 나눠 float 로 만들고 8388607 을 곱한 뒤 소수점을 버렸다. 그러면 아무 계산을
/// 하지 않아도 0 이 아닌 모든 샘플이 1 LSB 씩 깎인다 — FLAC 재생이 "Bit-Perfect" 로 표시되면서도
/// 실제로는 원본과 달랐다.
/// </summary>
public class PcmMathTests
{
    private static byte[] Pack24(params int[] values)
    {
        var pcm = new byte[values.Length * 3];
        for (var i = 0; i < values.Length; i++) PcmMath.Write24(pcm, i * 3, values[i]);
        return pcm;
    }

    [Fact]
    public void TwentyFourBitSurvivesFloatUnchanged()
    {
        // 양 끝, 0 근처, 그리고 전 구간을 성기게 훑는다.
        var values = new List<int> { 0, 1, -1, 2, -2, 255, -255, 8388607, -8388608, 4194304, -4194304, 1234567 };
        for (var v = -8388608; v <= 8388607; v += 4099) values.Add(v);

        var pcm = Pack24(values.ToArray());
        Assert.Equal(pcm, PcmMath.FromFloatExact(PcmMath.ToFloat(pcm, 24), 24));
    }

    [Fact]
    public void SixteenBitSurvivesFloatUnchanged()
    {
        var pcm = new byte[65536 * 2];
        // int 로 센다. short 로 세면 32767 다음이 -32768 이라 v <= short.MaxValue 가 늘 참이고 끝나지 않는다.
        for (int v = short.MinValue; v <= short.MaxValue; v++)
        {
            BitConverter.TryWriteBytes(pcm.AsSpan((v - short.MinValue) * 2), (short)v);
        }

        Assert.Equal(pcm, PcmMath.FromFloatExact(PcmMath.ToFloat(pcm, 16), 16));
    }

    /// <summary>16bit 를 24bit 로 넓히면 값에 256 을 곱한 것과 같다. 하위 바이트에 다른 게 섞이면 안 된다.</summary>
    [Theory]
    [InlineData((short)1)]
    [InlineData((short)-1)]
    [InlineData((short)100)]
    [InlineData(short.MaxValue)]
    [InlineData(short.MinValue)]
    public void WideningSixteenBitOnlyAddsZeroBits(short value)
    {
        var wide = PcmMath.Widen24(BitConverter.GetBytes(value), 16);
        Assert.Equal(value * 256, PcmMath.Read24(wide, 0));

        // float 를 거쳐 24bit 로 가도 같은 값이어야 한다(FLAC 16bit → 24bit 출력 경로).
        var viaFloat = PcmMath.FromFloatExact(PcmMath.ToFloat(BitConverter.GetBytes(value), 16), 24);
        Assert.Equal(value * 256, PcmMath.Read24(viaFloat, 0));
    }

    [Fact]
    public void ThirtyTwoBitKeepsItsTopTwentyFourBits()
    {
        var wide = PcmMath.Widen24(BitConverter.GetBytes(0x12345678), 32);
        Assert.Equal(0x123456, PcmMath.Read24(wide, 0));
    }

    [Fact]
    public void OutOfRangeFloatsClipInsteadOfWrapping()
    {
        var pcm = PcmMath.FromFloatExact([2f, -2f, float.NaN], 24);
        Assert.Equal(8388607, PcmMath.Read24(pcm, 0));
        Assert.Equal(-8388608, PcmMath.Read24(pcm, 3));
        Assert.Equal(0, PcmMath.Read24(pcm, 6));
    }

    /// <summary>삼각 분포 디더: ±1 LSB 안에 있고, 평균은 0, 분산은 1/6 LSB².</summary>
    [Fact]
    public void DitherIsTriangularWithinOneLsb()
    {
        var dither = new TpdfDither(12345);
        const int n = 200_000;
        double sum = 0, sumSq = 0;
        for (var i = 0; i < n; i++)
        {
            var d = dither.Next();
            Assert.InRange(d, -1.0, 1.0);
            sum += d;
            sumSq += d * d;
        }

        Assert.InRange(sum / n, -0.01, 0.01);
        Assert.InRange(sumSq / n, 1.0 / 6 - 0.01, 1.0 / 6 + 0.01);
    }

    /// <summary>
    /// 디더의 쓸모: 1 LSB 보다 작은 신호는 그냥 반올림하면 통째로 사라지지만(0 만 남는다),
    /// 디더를 걸면 평균으로 살아남는다.
    /// </summary>
    [Fact]
    public void DitherPreservesDetailBelowOneLsb()
    {
        var quiet = Enumerable.Repeat(0.3f / PcmMath.Scale16, 50_000).ToArray();   // 0.3 LSB

        var truncated = PcmMath.FromFloat(quiet, 16, null);
        Assert.All(Enumerable.Range(0, quiet.Length), i => Assert.Equal(0, BitConverter.ToInt16(truncated, i * 2)));

        var dithered = PcmMath.FromFloat(quiet, 16, new TpdfDither(7));
        var mean = Enumerable.Range(0, quiet.Length).Average(i => (double)BitConverter.ToInt16(dithered, i * 2));
        Assert.InRange(mean, 0.25, 0.35);
    }

    [Fact]
    public void UnityGainDoesNotTouchTheBytes()
    {
        var pcm = Pack24(1, -1, 1234567, -7654321);
        Assert.Same(pcm, PcmGain.Apply(pcm, 24, 2, 1f, 1f, new TpdfDither()));
    }

    /// <summary>볼륨을 바꾸면 조각 안에서 앞 게인에서 새 게인까지 이어진다. 계단이 없어야 지퍼 소리가 없다.</summary>
    [Fact]
    public void GainChangeIsRampedAcrossTheChunk()
    {
        const int frames = 1000;
        var samples = Enumerable.Repeat(0.5f, frames * 2).ToArray();
        PcmGain.Apply(samples, 2, 1f, 0.5f);

        // 좌우 채널은 같은 프레임에서 같은 게인을 받는다.
        for (var f = 0; f < frames; f++) Assert.Equal(samples[f * 2], samples[f * 2 + 1]);

        // 단조 감소하고, 한 프레임의 변화는 전체 변화의 1/frames 이다.
        for (var f = 1; f < frames; f++)
        {
            var step = samples[(f - 1) * 2] - samples[f * 2];
            Assert.InRange(step, 0f, 0.25f / frames * 1.01f);
        }

        Assert.Equal(0.25f, samples[^1], 5);

        // 다음 조각은 끝난 게인에서 그대로 이어진다.
        var next = Enumerable.Repeat(0.5f, 8).ToArray();
        PcmGain.Apply(next, 2, 0.5f, 0.5f);
        Assert.All(next, s => Assert.Equal(0.25f, s, 6));
    }

    /// <summary>줄인 값은 반올림 오차가 ±1 LSB(디더) 안에 있어야 한다. 예전에는 0 쪽으로 잘라 냈다.</summary>
    [Fact]
    public void AttenuationIsRoundedNotTruncated()
    {
        var pcm = Pack24(Enumerable.Range(1, 2000).Select(i => i * 4001).ToArray());
        var half = PcmGain.Apply(pcm, 24, 1, 0.5f, 0.5f, new TpdfDither(3));

        double error = 0;
        for (var i = 0; i < 2000; i++)
        {
            var exact = (i + 1) * 4001 * 0.5;
            var got = PcmMath.Read24(half, i * 3);
            Assert.InRange(got - exact, -1.6, 1.6);
            error += got - exact;
        }

        // 잘라 내면 평균 오차가 -0.25 LSB 쯤 된다. 반올림에 디더면 0 근처다.
        Assert.InRange(error / 2000, -0.08, 0.08);
    }

    /// <summary>
    /// 실제 FLAC 디코드 경로: 픽스처(16bit 스테레오)의 n초대 값은 왼쪽이 정확히 +(n+1)/8, 오른쪽이 -(n+1)/8 이다.
    /// 1 LSB 도, 부호(극성)도 틀리면 안 된다. 두 채널의 부호가 반대라는 것은 ffmpeg 로 디코드해 확인했다.
    /// </summary>
    [Theory]
    [InlineData(500, 1)]
    [InlineData(1500, 2)]
    [InlineData(2500, 3)]
    public void FlacDecodesToTheExactSampleValue(int positionMs, int eighths)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "seek-markers.flac");
        using var source = LocalFileRenderer.TryOpen(path);
        Assert.NotNull(source);

        var pcm = source.Read(positionMs, 20, out var format);

        Assert.Equal(24, format.depth);
        Assert.Equal(2, format.channels);
        Assert.True(pcm.Length >= 6);
        for (var i = 0; i + 5 < pcm.Length; i += 6)
        {
            Assert.Equal(eighths * 1048576, PcmMath.Read24(pcm, i));
            Assert.Equal(-eighths * 1048576, PcmMath.Read24(pcm, i + 3));
        }
    }
}
