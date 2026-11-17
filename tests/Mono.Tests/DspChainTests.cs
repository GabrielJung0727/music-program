using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// DSP 체인이 조각 경계를 넘어 이어지는가.
///
/// 오디오는 20~30ms 조각으로 흘러온다. 필터가 조각마다 처음부터 다시 시작하면 경계마다 파형이
/// 꺾이고, 그게 EQ 를 켰을 때 얹히던 잡음이다. 같은 신호를 한 번에 넣든 잘게 나눠 넣든 결과가
/// 같아야 한다 — 이 테스트들이 지키는 건 그것 하나다.
/// </summary>
public class DspChainTests
{
    private const int Rate = 44100;

    private static float[] Music(int frames, int channels = 2)
    {
        // 여러 주파수가 섞인, 조각 경계에서 0 이 아닌 신호.
        var x = new float[frames * channels];
        for (var f = 0; f < frames; f++)
        {
            var t = (double)f / Rate;
            var l = 0.3 * Math.Sin(2 * Math.PI * 220 * t) + 0.2 * Math.Sin(2 * Math.PI * 3100 * t);
            var r = 0.3 * Math.Sin(2 * Math.PI * 330 * t) + 0.1 * Math.Sin(2 * Math.PI * 9000 * t);
            x[f * channels] = (float)l;
            if (channels > 1) x[f * channels + 1] = (float)r;
        }

        return x;
    }

    private static DspSettings With(DspPresetKind preset, string? eq = null, string? profile = null)
        => new(true, preset, -3f, eq, 1.5f, 0.4f, -1f, 0.5f, profile, null);

    private static float[] InOnePiece(DspSettings s, float[] x, bool upsample)
        => new DspChain().Process((float[])x.Clone(), Rate, 2, s, upsample, out _);

    private static float[] InChunks(DspSettings s, float[] x, bool upsample, int[] chunkFrames)
    {
        var chain = new DspChain();
        var output = new List<float>();
        var at = 0;
        for (var i = 0; at < x.Length; i++)
        {
            var size = Math.Min(chunkFrames[i % chunkFrames.Length] * 2, x.Length - at);
            output.AddRange(chain.Process(x[at..(at + size)], Rate, 2, s, upsample, out _));
            at += size;
        }

        return output.ToArray();
    }

    public static IEnumerable<object[]> Presets() =>
    [
        [With(DspPresetKind.Parametric, """[{"f":120,"g":4,"q":0.8},{"f":3000,"g":-3,"q":1.4}]"""), false],
        [With(DspPresetKind.Headphones, profile: "harman"), false],
        [With(DspPresetKind.Headphones, profile: "harman"), true],
        [With(DspPresetKind.Speakers), false],
        [With(DspPresetKind.Speakers), true],
        [With(DspPresetKind.Crossfeed), false],
        [With(DspPresetKind.RoomIr), false],
    ];

    [Theory]
    [MemberData(nameof(Presets))]
    public void ChunkedProcessingMatchesOnePass(DspSettings settings, bool upsample)
    {
        var x = Music(Rate / 2);
        var whole = InOnePiece(settings, x, upsample);
        var pieces = InChunks(settings, x, upsample, [441, 882, 1323, 600, 1]);

        Assert.Equal(whole.Length, pieces.Length);
        for (var i = 0; i < whole.Length; i++)
        {
            Assert.True(Math.Abs(whole[i] - pieces[i]) < 1e-6f, $"샘플 {i} 에서 갈라진다: {whole[i]} vs {pieces[i]}");
        }
    }

    [Fact]
    public void DisabledChainReturnsTheInputUntouched()
    {
        var x = Music(100);
        var chain = new DspChain();

        Assert.Same(x, chain.Process(x, Rate, 2, DspSettings.Off, true, out var rate));
        Assert.Equal(Rate, rate);
        Assert.Same(x, chain.Process(x, Rate, 2, With(DspPresetKind.Off), true, out _));
    }

    [Fact]
    public void AudiophileRoomsNeverProcess()
    {
        var room = ListeningRoom.ForMode("r", "a", "h", RoomMode.Audiophile);
        room.DspEnabled = true;
        room.DspPreset = DspPresetKind.Parametric;

        Assert.False(DspSettings.From(room).Active);
    }

    /// <summary>EQ 를 실제 샘플레이트로 설계한다. +6dB 를 건 대역의 사인파는 2배가 되어 나와야 한다.</summary>
    [Theory]
    [InlineData(44100)]
    [InlineData(96000)]
    public void PeakingEqHitsItsGainAtTheCentreFrequency(int rate)
    {
        var settings = new DspSettings(true, DspPresetKind.Parametric, 0, """[{"f":1000,"g":6.0206,"q":1}]""", 0, 0, 0, 0, null, null);
        var x = new float[rate];
        for (var i = 0; i < x.Length; i++) x[i] = (float)(0.25 * Math.Sin(2 * Math.PI * 1000 * i / rate));

        var y = new DspChain().Process(x, rate, 1, settings, false, out _);

        // 필터가 자리를 잡은 뒤(뒤쪽 절반)의 최댓값을 본다.
        var peak = y.Skip(rate / 2).Max(Math.Abs);
        Assert.InRange(peak, 0.49f, 0.51f);
    }

    [Fact]
    public void UpsamplerPassesTheOriginalSamplesThroughUntouched()
    {
        var up = new HalfBandUpsampler(1);
        var x = Music(4000, 1);
        var y = up.Process((float[])x.Clone());

        Assert.Equal(x.Length * 2, y.Length);
        // 짝수 출력은 원본을 HalfTaps 만큼 늦춘 것 그대로다.
        for (var n = HalfBandUpsampler.HalfTaps; n < x.Length; n++)
        {
            Assert.Equal(x[n - HalfBandUpsampler.HalfTaps], y[n * 2]);
        }
    }

    [Fact]
    public void UpsamplerKeepsDcAndLevel()
    {
        var dc = new HalfBandUpsampler(1).Process(Enumerable.Repeat(0.5f, 2000).ToArray());
        Assert.All(dc.Skip(200), s => Assert.Equal(0.5f, s, 4));

        // 1kHz 사인: 사이에 끼운 샘플까지 포함해 실효값이 그대로여야 한다.
        var x = new float[Rate];
        for (var i = 0; i < x.Length; i++) x[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / Rate));
        var y = new HalfBandUpsampler(1).Process(x);
        var rms = Math.Sqrt(y.Skip(1000).Average(s => (double)s * s));
        Assert.InRange(rms, 0.5 / Math.Sqrt(2) * 0.999, 0.5 / Math.Sqrt(2) * 1.001);
    }

    /// <summary>
    /// 업샘플링의 품질은 거울상이 얼마나 눌렸느냐다. 10kHz 를 2배로 올리면 34.1kHz 에 거울상이 생긴다.
    /// 직선으로 잇는 방식은 그걸 10dB 남짓밖에 못 누른다. 하프밴드 필터는 70dB 넘게 누른다.
    /// </summary>
    [Fact]
    public void UpsamplerSuppressesTheImage()
    {
        const double tone = 10_000;
        var x = new float[Rate];
        for (var i = 0; i < x.Length; i++) x[i] = (float)(0.5 * Math.Sin(2 * Math.PI * tone * i / Rate));

        var y = new HalfBandUpsampler(1).Process(x).Skip(2000).ToArray();
        var outRate = Rate * 2;

        var wanted = Magnitude(y, tone, outRate);
        var image = Magnitude(y, Rate - tone, outRate);

        Assert.InRange(wanted, 0.49, 0.51);
        Assert.True(20 * Math.Log10(image / wanted) < -70, $"거울상이 {20 * Math.Log10(image / wanted):F1}dB 로 남았다");
    }

    /// <summary>한 주파수의 진폭(단일 빈 DFT).</summary>
    private static double Magnitude(float[] y, double freq, int rate)
    {
        double re = 0, im = 0;
        for (var i = 0; i < y.Length; i++)
        {
            var w = 2 * Math.PI * freq * i / rate;
            // 한 창으로 측면 누설을 줄인다.
            var window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (y.Length - 1));
            re += y[i] * window * Math.Cos(w);
            im -= y[i] * window * Math.Sin(w);
        }

        return 2 * Math.Sqrt(re * re + im * im) / (y.Length * 0.5);
    }

    [Fact]
    public void ResetClearsTheFiltersMemory()
    {
        var settings = With(DspPresetKind.Parametric, """[{"f":100,"g":6,"q":0.7}]""");
        var chain = new DspChain();
        var x = Music(2000);

        var first = chain.Process((float[])x.Clone(), Rate, 2, settings, false, out _);
        chain.Reset();
        var again = chain.Process((float[])x.Clone(), Rate, 2, settings, false, out _);

        Assert.Equal(first, again);
    }
}
