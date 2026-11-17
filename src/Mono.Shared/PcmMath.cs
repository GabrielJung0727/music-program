using System.Buffers.Binary;

namespace Mono.Shared;

/// <summary>
/// TPDF(삼각 분포) 디더.
///
/// 계산을 거친 신호를 정수 눈금으로 되돌릴 때 그냥 반올림하면 오차가 신호를 따라 움직인다.
/// 그게 양자화 왜곡이다 — 작은 소리일수록 음악과 함께 일그러지는 소리로 들린다. 반올림하기
/// 전에 ±1 LSB 삼각 분포 잡음을 더하면 오차가 신호와 무관한 고른 잡음이 된다. 잡음은 조금
/// 늘지만(24bit 에서 -141dBFS) 왜곡은 사라진다.
///
/// 계산을 거치지 않은 신호에는 쓰지 않는다. 손대지 않은 샘플은 그대로 나가야 한다.
/// </summary>
public sealed class TpdfDither(uint seed = 0x9E3779B9)
{
    private uint _state = seed == 0 ? 0x9E3779B9 : seed;

    private double Uniform()
    {
        // xorshift32. 암호용이 아니라 소리에 실릴 잡음이라 빠르고 주기가 길면 된다.
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x * (1.0 / 4294967296.0);
    }

    /// <summary>(-1, +1) LSB 삼각 분포 값 하나.</summary>
    public double Next() => Uniform() - Uniform();
}

/// <summary>
/// 정수 PCM 과 float 사이의 변환.
///
/// 한 가지 눈금만 쓴다: 16bit 는 32768, 24bit 는 8388608. 나갈 때와 들어올 때의 눈금이 다르면
/// (예전에는 8388608 로 나누고 8388607 을 곱한 뒤 소수점을 버렸다) 아무 계산을 하지 않아도
/// 모든 샘플이 1 LSB 씩 깎인다. 같은 눈금으로 반올림하면 24bit 이하의 정수는 float32 를
/// 거쳐도 한 비트도 달라지지 않는다.
/// </summary>
public static class PcmMath
{
    public const float Scale16 = 32768f;
    public const float Scale24 = 8388608f;

    /// <summary>16 또는 24bit 리틀엔디안 정수 PCM → float(-1.0 ~ +1.0). 손실 없음.</summary>
    public static float[] ToFloat(ReadOnlySpan<byte> pcm, int bits)
    {
        if (bits <= 16)
        {
            var n = pcm.Length / 2;
            var f = new float[n];
            for (var i = 0; i < n; i++)
            {
                f[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i * 2, 2)) / Scale16;
            }

            return f;
        }

        var count = pcm.Length / 3;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = Read24(pcm, i * 3) / Scale24;
        }

        return samples;
    }

    /// <summary>
    /// float → 정수 PCM. 계산을 거치지 않은 신호용 — 디더 없이 같은 눈금으로 반올림하므로
    /// <see cref="ToFloat"/> 의 정확한 역이다.
    /// </summary>
    public static byte[] FromFloatExact(ReadOnlySpan<float> samples, int bits)
        => FromFloat(samples, bits, null);

    /// <summary>float → 정수 PCM. 계산을 거친 신호는 디더를 넘겨 양자화 왜곡을 잡음으로 바꾼다.</summary>
    public static byte[] FromFloat(ReadOnlySpan<float> samples, int bits, TpdfDither? dither)
    {
        if (bits <= 16)
        {
            var data = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var v = Quantize(samples[i] * (double)Scale16, dither, short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2, 2), (short)v);
            }

            return data;
        }

        var bytes = new byte[samples.Length * 3];
        for (var i = 0; i < samples.Length; i++)
        {
            Write24(bytes, i * 3, Quantize(samples[i] * (double)Scale24, dither, -8388608, 8388607));
        }

        return bytes;
    }

    /// <summary>
    /// 정수 PCM 을 24bit 패킹으로 넓힌다. 16bit 는 하위 바이트를 0 으로 채우므로 값은 그대로다.
    /// 32bit 는 상위 24비트만 남긴다.
    /// </summary>
    public static byte[] Widen24(ReadOnlySpan<byte> pcm, int sourceBits)
    {
        switch (sourceBits)
        {
            case 24:
                return pcm.ToArray();

            case 16:
            {
                var n = pcm.Length / 2;
                var wide = new byte[n * 3];
                for (var i = 0; i < n; i++)
                {
                    wide[i * 3 + 1] = pcm[i * 2];
                    wide[i * 3 + 2] = pcm[i * 2 + 1];
                }

                return wide;
            }

            case 32:
            {
                var n = pcm.Length / 4;
                var packed = new byte[n * 3];
                for (var i = 0; i < n; i++)
                {
                    packed[i * 3] = pcm[i * 4 + 1];
                    packed[i * 3 + 1] = pcm[i * 4 + 2];
                    packed[i * 3 + 2] = pcm[i * 4 + 3];
                }

                return packed;
            }

            default:
                throw new NotSupportedException($"{sourceBits}bit 정수 PCM 은 24bit 로 넓힐 수 없습니다.");
        }
    }

    public static int Read24(ReadOnlySpan<byte> pcm, int at)
    {
        var v = pcm[at] | (pcm[at + 1] << 8) | (pcm[at + 2] << 16);
        return (v & 0x800000) != 0 ? v | unchecked((int)0xFF000000) : v;
    }

    public static void Write24(Span<byte> pcm, int at, int value)
    {
        pcm[at] = (byte)value;
        pcm[at + 1] = (byte)(value >> 8);
        pcm[at + 2] = (byte)(value >> 16);
    }

    private static int Quantize(double scaled, TpdfDither? dither, int min, int max)
    {
        if (double.IsNaN(scaled)) return 0;
        if (dither is not null) scaled += dither.Next();
        return (int)Math.Clamp(Math.Round(scaled), min, max);
    }
}

/// <summary>
/// 디지털 볼륨.
///
/// 두 가지를 지킨다. 하나는 디더 — 줄인 결과를 정수로 되돌릴 때 그냥 잘라 내면 작은 볼륨에서
/// 양자화 왜곡이 들린다. 다른 하나는 경사 — 조각마다 게인이 계단처럼 바뀌면 볼륨을 끄는 동안
/// 지퍼 소리가 난다. 앞 조각의 게인에서 이번 게인까지 프레임 단위로 이어 준다.
/// </summary>
public static class PcmGain
{
    public static float FromPercent(int percent) => Math.Clamp(percent, 0, 100) / 100f;

    /// <summary>게인이 처음부터 끝까지 1 이면 배열을 그대로 돌려준다 — 비트퍼펙트 경로를 건드리지 않는다.</summary>
    public static byte[] Apply(byte[] pcm, int bits, int channels, float fromGain, float toGain, TpdfDither dither)
    {
        if (pcm.Length == 0 || (fromGain == 1f && toGain == 1f)) return pcm;

        var samples = PcmMath.ToFloat(pcm, bits);
        Apply(samples, channels, fromGain, toGain);
        return PcmMath.FromFloat(samples, bits, dither);
    }

    /// <summary>float 신호에 그 자리에서 게인을 건다. 정수로 되돌리지 않으므로 디더가 필요 없다.</summary>
    public static void Apply(Span<float> samples, int channels, float fromGain, float toGain)
    {
        var ch = Math.Max(1, channels);
        var frames = samples.Length / ch;
        if (frames == 0) return;

        if (fromGain == toGain)
        {
            if (toGain == 1f) return;
            for (var i = 0; i < samples.Length; i++) samples[i] *= toGain;
            return;
        }

        var step = (toGain - fromGain) / frames;
        for (var f = 0; f < frames; f++)
        {
            var g = fromGain + step * (f + 1);
            for (var c = 0; c < ch; c++) samples[f * ch + c] *= g;
        }
    }
}
