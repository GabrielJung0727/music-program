using System.Collections.Concurrent;
using System.Text.Json;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// Core 전용 DSP. Control/채팅 스레드와 공유 락을 쓰지 않는다.
/// Audiophile·DSP Off이면 입력을 그대로 반환(Bit-perfect).
/// </summary>
public static class DspPipeline
{
    private static readonly ConcurrentDictionary<string, float[]> IrCache = new();

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

        if (room.HeadroomDb < 0)
        {
            var g = MathF.Pow(10f, room.HeadroomDb / 20f);
            for (var i = 0; i < samples.Length; i++) samples[i] *= g;
        }

        switch (room.DspPreset)
        {
            case DspPresetKind.Headphones:
                samples = TiltEq(samples, channels, 0.04f);
                samples = ApplyDeviceEq(samples, channels, room.DeviceEqProfile);
                break;
            case DspPresetKind.Speakers:
                samples = TiltEq(samples, channels, -0.02f);
                samples = ApplySpeakerSetup(samples, channels, sampleRate, room);
                break;
            case DspPresetKind.Crossfeed:
                samples = Crossfeed(samples, channels, 0.35f);
                break;
            case DspPresetKind.RoomIr:
                samples = Convolve(samples, channels, LoadIr(room.ConvolutionIrPath));
                break;
            case DspPresetKind.Parametric:
                samples = ApplyEasyEq(samples, channels, sampleRate, room.EasyEqJson);
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

    public static byte[] ApplyGain(byte[] pcm, int bitDepth, int volumePercent)
    {
        if (volumePercent >= 100 || pcm.Length == 0) return pcm;
        var gain = Math.Clamp(volumePercent, 0, 100) / 100f;
        var samples = ToFloat(pcm, bitDepth);
        for (var i = 0; i < samples.Length; i++) samples[i] *= gain;
        return FromFloat(samples, bitDepth);
    }

    public static float[] ToFloat(byte[] pcm, int bitDepth)
    {
        if (bitDepth == 16)
        {
            var n = pcm.Length / 2;
            var f = new float[n];
            for (var i = 0; i < n; i++)
                f[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
            return f;
        }

        var count = pcm.Length / 3;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var v = pcm[i * 3] | (pcm[i * 3 + 1] << 8) | (pcm[i * 3 + 2] << 16);
            if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
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

    public static float[] LoadIr(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return [0.92f, 0.06f, 0.015f, 0.005f];

        return IrCache.GetOrAdd(path, p =>
        {
            try
            {
                if (p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(p);
                    var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
                    if (entry is null) return [0.92f, 0.06f, 0.015f, 0.005f];
                    var tmp = Path.Combine(Path.GetTempPath(), "mono-ir-" + Guid.NewGuid().ToString("n") + ".wav");
                    using (var src = entry.Open())
                    using (var dst = File.Create(tmp))
                        src.CopyTo(dst);
                    try { return LoadWavIr(tmp); }
                    finally { try { File.Delete(tmp); } catch { /* ignore */ } }
                }
                return LoadWavIr(p);
            }
            catch
            {
                return [0.92f, 0.06f, 0.015f, 0.005f];
            }
        });
    }

    private static float[] LoadWavIr(string path)
    {
        using var slicer = new WavSlicer(path);
        var pcm = slicer.Read(0, (int)Math.Min(slicer.DurationMs, 2000));
        var f = ToFloat(pcm, slicer.Format.BitDepth);
        // mono mixdown, truncate
        var ch = Math.Max(slicer.Format.Channels, 1);
        var frames = f.Length / ch;
        var mono = new float[Math.Min(frames, 8192)];
        for (var i = 0; i < mono.Length; i++)
        {
            float s = 0;
            for (var c = 0; c < ch; c++) s += f[i * ch + c];
            mono[i] = s / ch;
        }
        return mono.Length > 0 ? mono : [0.92f, 0.06f, 0.015f, 0.005f];
    }

    private static float[] ApplyEasyEq(float[] x, int ch, int sampleRate, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return x;
        try
        {
            var bands = JsonSerializer.Deserialize<List<EqBand>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (bands is null || bands.Count == 0) return x;
            var y = x;
            foreach (var b in bands.Take(16))
                y = PeakingEq(y, ch, sampleRate, b.F, b.G, Math.Max(0.1f, b.Q));
            return y;
        }
        catch { return x; }
    }

    private sealed class EqBand
    {
        public float F { get; set; } = 1000;
        public float G { get; set; }
        public float Q { get; set; } = 1;
    }

    /// <summary>RBJ peaking EQ (직접형 II).</summary>
    private static float[] PeakingEq(float[] x, int ch, int fs, float freq, float gainDb, float q)
    {
        ch = Math.Max(ch, 1);
        var A = MathF.Pow(10f, gainDb / 40f);
        var w0 = 2 * MathF.PI * Math.Clamp(freq, 20, fs / 2f - 1) / fs;
        var alpha = MathF.Sin(w0) / (2 * q);
        var b0 = 1 + alpha * A;
        var b1 = -2 * MathF.Cos(w0);
        var b2 = 1 - alpha * A;
        var a0 = 1 + alpha / A;
        var a1 = -2 * MathF.Cos(w0);
        var a2 = 1 - alpha / A;
        b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;

        var y = new float[x.Length];
        var z1 = new float[ch];
        var z2 = new float[ch];
        for (var i = 0; i < x.Length; i++)
        {
            var c = i % ch;
            var xin = x[i];
            var outv = b0 * xin + z1[c];
            z1[c] = b1 * xin - a1 * outv + z2[c];
            z2[c] = b2 * xin - a2 * outv;
            y[i] = outv;
        }
        return y;
    }

    private static float[] ApplySpeakerSetup(float[] x, int ch, int sampleRate, ListeningRoom room)
    {
        if (ch < 2) return x;
        var delayL = (int)Math.Clamp(room.SpeakerDelayMsLeft * sampleRate / 1000f, 0, sampleRate);
        var delayR = (int)Math.Clamp(room.SpeakerDelayMsRight * sampleRate / 1000f, 0, sampleRate);
        var gL = MathF.Pow(10f, room.SpeakerGainLeftDb / 20f);
        var gR = MathF.Pow(10f, room.SpeakerGainRightDb / 20f);
        var frames = x.Length / ch;
        var y = new float[x.Length];
        for (var f = 0; f < frames; f++)
        {
            var srcL = f - delayL;
            var srcR = f - delayR;
            y[f * ch] = (srcL >= 0 ? x[srcL * ch] : 0) * gL;
            y[f * ch + 1] = (srcR >= 0 ? x[srcR * ch + 1] : 0) * gR;
            for (var c = 2; c < ch; c++) y[f * ch + c] = x[f * ch + c];
        }
        return y;
    }

    private static float[] ApplyDeviceEq(float[] x, int ch, string? profile)
    {
        // 간단 프로파일: harman / bright / warm
        return profile?.ToLowerInvariant() switch
        {
            "harman" => PeakingEq(PeakingEq(x, ch, 48000, 100, 2f, 0.7f), ch, 48000, 3000, 1.5f, 1.2f),
            "bright" => PeakingEq(x, ch, 48000, 8000, 3f, 0.9f),
            "warm" => PeakingEq(x, ch, 48000, 200, 2.5f, 0.8f),
            _ => x
        };
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
        if (ch < 2) return x;
        var y = new float[x.Length];
        for (var i = 0; i + 1 < x.Length; i += ch)
        {
            var l = x[i];
            var r = x[i + 1];
            y[i] = l * (1 - mix) + r * mix * 0.5f;
            y[i + 1] = r * (1 - mix) + l * mix * 0.5f;
            for (var c = 2; c < ch; c++) y[i + c] = x[i + c];
        }
        return y;
    }

    private static float[] Convolve(float[] x, int ch, float[] ir)
    {
        ch = Math.Max(ch, 1);
        var y = new float[x.Length];
        var tapStep = ch;
        var maxTaps = Math.Min(ir.Length, 2048);
        for (var i = 0; i < x.Length; i++)
        {
            float acc = 0;
            for (var k = 0; k < maxTaps; k++)
            {
                var j = i - k * tapStep;
                if (j >= 0) acc += x[j] * ir[k];
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
