using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// Core 전용 DSP. Control/채팅 스레드와 공유 락을 쓰지 않는다.
/// Audiophile·DSP Off이면 입력을 그대로 반환(Bit-perfect).
/// </summary>
public static class DspPipeline
{
    public static byte[] Process(
        byte[] pcm,
        int sampleRate,
        int bitDepth,
        int channels,
        ListeningRoom room,
        out int outRate,
        out int outDepth,
        out bool srcApplied)
    {
        outRate = sampleRate;
        outDepth = bitDepth;
        srcApplied = false;
        if (!room.DspEnabled || room.DspPreset == DspPresetKind.Off || room.Mode == RoomMode.Audiophile)
        {
            return pcm;
        }

        var samples = ToFloat(pcm, bitDepth);
        switch (room.DspPreset)
        {
            case DspPresetKind.Headphones:
                samples = TiltEq(samples, channels, 0.04f);
                break;
            case DspPresetKind.Speakers:
                samples = TiltEq(samples, channels, -0.02f);
                break;
            case DspPresetKind.Crossfeed:
                samples = Crossfeed(samples, channels, 0.35f);
                break;
            case DspPresetKind.RoomIr:
                samples = ConvolveLight(samples, channels);
                break;
        }

        if (room.DspPreset is DspPresetKind.Headphones or DspPresetKind.Speakers)
        {
            samples = Upsample2(samples, channels);
            outRate = sampleRate * 2;
            srcApplied = true;
        }

        outDepth = 24;
        return FromFloat(samples, 24);
    }

    /// <summary>
    /// 디지털 볼륨 감쇠. Bit-perfect 경로를 깨뜨리므로 하드웨어 볼륨이 없는 기기에만,
    /// 그리고 Audiophile 모드가 아닐 때만 호출한다.
    /// </summary>
    public static byte[] ApplyGain(byte[] pcm, int bitDepth, int volumePercent)
    {
        if (volumePercent >= 100 || pcm.Length == 0)
        {
            return pcm;
        }

        var gain = Math.Clamp(volumePercent, 0, 100) / 100f;
        var samples = ToFloat(pcm, bitDepth);
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;
        }

        return FromFloat(samples, bitDepth);
    }

    public static float[] ToFloat(byte[] pcm, int bitDepth)
    {
        if (bitDepth == 16)
        {
            var n = pcm.Length / 2;
            var f = new float[n];
            for (var i = 0; i < n; i++)
            {
                f[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
            }

            return f;
        }

        var count = pcm.Length / 3;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var v = pcm[i * 3] | (pcm[i * 3 + 1] << 8) | (pcm[i * 3 + 2] << 16);
            if ((v & 0x800000) != 0)
            {
                v |= unchecked((int)0xFF000000);
            }

            samples[i] = v / 8388608f;
        }

        return samples;
    }

    public static byte[] FromFloat(float[] samples, int bitDepth)
    {
        if (bitDepth == 16)
        {
            var data = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var v = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
                BitConverter.TryWriteBytes(data.AsSpan(i * 2), v);
            }

            return data;
        }

        var bytes = new byte[samples.Length * 3];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (int)Math.Clamp(samples[i] * 8388607f, -8388608, 8388607);
            bytes[i * 3] = (byte)v;
            bytes[i * 3 + 1] = (byte)(v >> 8);
            bytes[i * 3 + 2] = (byte)(v >> 16);
        }

        return bytes;
    }

    private static float[] TiltEq(float[] x, int ch, float amount)
    {
        var y = new float[x.Length];
        var state = new float[Math.Max(ch, 1)];
        for (var i = 0; i < x.Length; i++)
        {
            var c = ch <= 0 ? 0 : i % ch;
            state[c] = state[c] * 0.95f + x[i] * 0.05f;
            y[i] = x[i] + state[c] * amount;
        }

        return y;
    }

    private static float[] Crossfeed(float[] x, int ch, float mix)
    {
        if (ch < 2)
        {
            return x;
        }

        var y = new float[x.Length];
        for (var i = 0; i + 1 < x.Length; i += ch)
        {
            var l = x[i];
            var r = x[i + 1];
            y[i] = l * (1 - mix) + r * mix * 0.5f;
            y[i + 1] = r * (1 - mix) + l * mix * 0.5f;
            for (var c = 2; c < ch; c++)
            {
                y[i + c] = x[i + c];
            }
        }

        return y;
    }

    private static float[] ConvolveLight(float[] x, int ch)
    {
        float[] ir = [0.92f, 0.06f, 0.015f, 0.005f];
        var y = new float[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            float acc = 0;
            for (var k = 0; k < ir.Length; k++)
            {
                var j = i - k * Math.Max(ch, 1);
                if (j >= 0)
                {
                    acc += x[j] * ir[k];
                }
            }

            y[i] = acc;
        }

        return y;
    }

    private static float[] Upsample2(float[] x, int ch)
    {
        ch = Math.Max(ch, 1);
        var frames = x.Length / ch;
        var y = new float[frames * 2 * ch];
        for (var f = 0; f < frames - 1; f++)
        {
            for (var c = 0; c < ch; c++)
            {
                var a = x[f * ch + c];
                var b = x[(f + 1) * ch + c];
                y[f * 2 * ch + c] = a;
                y[(f * 2 + 1) * ch + c] = (a + b) * 0.5f;
            }
        }

        return y;
    }
}
