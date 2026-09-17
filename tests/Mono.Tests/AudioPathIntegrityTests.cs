using Mono.Core;
using Mono.Output;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 디코더에서 장치 큐까지 오는 동안 파형이 그대로인가.
/// 여기서 어긋나면 사용자는 음악 위에 얹힌 잡음으로 듣는다.
/// </summary>
public class AudioPathIntegrityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mono-audio-" + Guid.NewGuid().ToString("n")[..8]);

    public AudioPathIntegrityTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* 지워지지 않아도 테스트 결과와 무관 */ }
    }

    /// <summary>bitsPerSample 그대로의 PCM WAV 하나. 32bit 는 float(포맷 태그 3).</summary>
    private string WriteWav(string name, int rate, int bits, int channels, int frames, bool isFloat = false)
    {
        var bpf = channels * (bits / 8);
        var data = new byte[frames * bpf];
        for (var n = 0; n < frames; n++)
        {
            var s = Math.Sin(2 * Math.PI * 440.0 * n / rate) * 0.5;
            for (var c = 0; c < channels; c++)
            {
                var i = (n * channels + c) * (bits / 8);
                switch (bits)
                {
                    case 16:
                        BitConverter.TryWriteBytes(data.AsSpan(i), (short)(s * short.MaxValue));
                        break;
                    case 24:
                        var v24 = (int)(s * 8388607);
                        data[i] = (byte)v24; data[i + 1] = (byte)(v24 >> 8); data[i + 2] = (byte)(v24 >> 16);
                        break;
                    case 32 when isFloat:
                        BitConverter.TryWriteBytes(data.AsSpan(i), (float)s);
                        break;
                    case 32:
                        BitConverter.TryWriteBytes(data.AsSpan(i), (int)(s * int.MaxValue));
                        break;
                }
            }
        }

        var path = Path.Combine(_dir, name);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write("RIFF"u8); bw.Write(36 + data.Length); bw.Write("WAVE"u8);
        bw.Write("fmt "u8); bw.Write(16);
        bw.Write((short)(isFloat ? 3 : 1));
        bw.Write((short)channels); bw.Write(rate);
        bw.Write(rate * bpf); bw.Write((short)bpf); bw.Write((short)bits);
        bw.Write("data"u8); bw.Write(data.Length);
        bw.Write(data);
        return path;
    }

    /// <summary>
    /// Clock-sync 렌더 루프는 버퍼 깊이에 따라 10~30ms 사이의 들쭉날쭉한 길이를 요청한다.
    /// 그렇게 잘라 읽어도 이어 붙인 결과는 원본과 한 바이트도 다르면 안 된다 —
    /// 한 프레임이라도 건너뛰면 그 자리에 딸깍 소리가 남고, 초당 수십 번이면 지지직거린다.
    /// </summary>
    [Fact]
    public void LocalWavReadsAreGaplessAcrossVariableChunkLengths()
    {
        const int rate = 44100, bits = 16, channels = 2, frames = rate * 3;
        var path = WriteWav("gapless.wav", rate, bits, channels, frames);
        var expected = File.ReadAllBytes(path)[44..];

        using var source = LocalFileRenderer.TryOpen(path)!;
        Assert.NotNull(source);

        var got = new List<byte>();
        var want = new[] { 30, 30, 13, 17, 10, 23, 30, 11, 19, 27, 13, 16, 21, 10, 30 };
        var mediaNow = 0L;
        for (var i = 0; got.Count < expected.Length && i < 4000; i++)
        {
            var dur = want[i % want.Length];
            var chunk = source.Read(mediaNow, dur, out _);
            if (chunk.Length == 0) break;
            got.AddRange(chunk);
            mediaNow += dur;
        }

        var n = Math.Min(got.Count, expected.Length);
        Assert.True(n > rate * channels * (bits / 8), $"읽은 양이 너무 적다: {n}");
        var firstDiff = -1;
        for (var i = 0; i < n; i++)
        {
            if (got[i] != expected[i]) { firstDiff = i; break; }
        }

        Assert.True(firstDiff < 0,
            $"파형이 원본과 어긋난다 — 바이트 {firstDiff} (프레임 {firstDiff / (channels * bits / 8)}) 부터 다르다.");
    }

    /// <summary>
    /// 소스가 알려 준 비트뎁스와 실제 바이트 수는 반드시 맞아야 한다.
    /// 32bit 파일을 24bit 라고 말하면서 4바이트짜리 샘플을 넘기면 장치는 그 바이트열을
    /// 3바이트 단위로 읽는다 — 모든 샘플이 밀려 소리 전체가 잡음이 된다.
    /// </summary>
    [Theory]
    [InlineData(16, false)]
    [InlineData(24, false)]
    [InlineData(32, false)]
    [InlineData(32, true)]
    public void DeclaredDepthMatchesTheBytesHandedToTheDevice(int bits, bool isFloat)
    {
        const int rate = 44100, channels = 2, frames = 4410;
        var path = WriteWav($"depth{bits}{(isFloat ? "f" : "")}.wav", rate, bits, channels, frames, isFloat);

        using var source = LocalFileRenderer.TryOpen(path)!;
        var chunk = source.Read(0, 20, out var fmt);

        Assert.True(chunk.Length > 0, "청크가 비었다");
        var bytesPerFrame = chunk.Length / (20 * rate / 1000);
        Assert.Equal(channels * (fmt.depth / 8), bytesPerFrame);

        // 장치 계층이 실제로 여는 규격 — 여기서 뎁스를 접으면 바이트 수도 함께 접혀야 한다.
        var deviceBits = fmt.depth >= 24 ? 24 : 16;
        Assert.Equal(channels * (deviceBits / 8), bytesPerFrame);
    }

    /// <summary>Core 팬아웃 슬라이서도 같은 계약을 지켜야 한다.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void FanOutSlicerDepthMatchesItsBytes(int bits)
    {
        const int rate = 48000, channels = 2, frames = 4800;
        var path = WriteWav($"fanout{bits}.wav", rate, bits, channels, frames, isFloat: bits == 32);

        using var slicer = new WavSlicer(path);
        var pcm = slicer.Read(0, 20);
        var bytesPerFrame = pcm.Length / (20 * rate / 1000);

        Assert.Equal(channels * (slicer.Format.BitDepth / 8), bytesPerFrame);

        var deviceBits = slicer.Format.BitDepth >= 24 ? 24 : 16;
        Assert.Equal(channels * (deviceBits / 8), bytesPerFrame);
    }
}
