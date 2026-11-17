using System.Collections.Concurrent;
using System.Text.Json;

namespace Mono.Shared;

/// <summary>룸에 걸린 DSP 설정의 사본. 값이 같으면 같은 설정이다 — 체인은 이게 바뀔 때만 다시 짠다.</summary>
public sealed record DspSettings(
    bool Enabled,
    DspPresetKind Preset,
    float HeadroomDb,
    string? EasyEqJson,
    float SpeakerDelayMsLeft,
    float SpeakerDelayMsRight,
    float SpeakerGainLeftDb,
    float SpeakerGainRightDb,
    string? DeviceEqProfile,
    string? ConvolutionIrPath)
{
    public static DspSettings Off { get; } = new(false, DspPresetKind.Off, 0, null, 0, 0, 0, 0, null, null);

    /// <summary>신호를 실제로 건드리는가. 아니면 원본이 그대로 나가야 한다.</summary>
    public bool Active => Enabled && Preset != DspPresetKind.Off;

    /// <summary>이 프리셋은 2배 업샘플링을 함께 한다.</summary>
    public bool Upsamples => Active && Preset is DspPresetKind.Headphones or DspPresetKind.Speakers;

    /// <summary>오디오파일 룸은 설정이 무엇이든 가공하지 않는다.</summary>
    public static DspSettings From(ListeningRoom room)
        => room.Mode == RoomMode.Audiophile
            ? Off
            : new DspSettings(
                room.DspEnabled, room.DspPreset, room.HeadroomDb, room.EasyEqJson,
                room.SpeakerDelayMsLeft, room.SpeakerDelayMsRight,
                room.SpeakerGainLeftDb, room.SpeakerGainRightDb,
                room.DeviceEqProfile, room.ConvolutionIrPath);
}

/// <summary>
/// DSP 체인. 조각 사이에 상태를 이어 간다.
///
/// 오디오는 20~30ms 조각으로 흘러온다. 필터를 조각마다 새로 시작하면(예전 구현이 그랬다)
/// IIR 필터의 기억이 조각 경계마다 0 으로 돌아가 그 자리에서 파형이 꺾이고, 컨볼루션은 앞 조각의
/// 꼬리를 잃는다. 초당 수십 번 생기는 그 꺾임이 EQ 를 켜면 얹히던 잡음이다. 필터 상태와 지연선을
/// 인스턴스가 들고 있다가 다음 조각에 이어 쓴다.
///
/// 스트림 하나에 인스턴스 하나. 탐색하거나 곡이 끊겨 바뀌면 <see cref="Reset"/> 으로 기억을 지운다.
/// </summary>
public sealed class DspChain
{
    private readonly List<IStage> _stages = [];
    private HalfBandUpsampler? _upsampler;
    private DspSettings _settings = DspSettings.Off;
    private int _rate;
    private int _channels;
    private bool _upsample;

    /// <summary>필터의 기억을 지운다. 앞뒤가 이어지지 않는 자리(탐색, 곡 건너뛰기)에서 부른다.</summary>
    public void Reset()
    {
        foreach (var stage in _stages) stage.Reset();
        _upsampler?.Reset();
    }

    /// <summary>
    /// 인터리브된 float 신호를 가공한다. 입력 배열을 그 자리에서 고쳐 쓴다.
    /// 설정이 꺼져 있으면 아무것도 하지 않고 그대로 돌려준다.
    /// </summary>
    public float[] Process(float[] samples, int sampleRate, int channels, DspSettings settings, bool allowUpsample, out int outRate)
    {
        outRate = sampleRate;
        if (!settings.Active || samples.Length == 0 || sampleRate <= 0) return samples;

        var ch = Math.Max(1, channels);
        var upsample = allowUpsample && settings.Upsamples;
        if (settings != _settings || sampleRate != _rate || ch != _channels || upsample != _upsample)
        {
            Build(settings, sampleRate, ch, upsample);
        }

        foreach (var stage in _stages) stage.Process(samples);

        if (_upsampler is null) return samples;
        outRate = sampleRate * 2;
        return _upsampler.Process(samples);
    }

    private void Build(DspSettings s, int rate, int ch, bool upsample)
    {
        _settings = s;
        _rate = rate;
        _channels = ch;
        _upsample = upsample;
        _stages.Clear();

        if (s.HeadroomDb < 0) _stages.Add(new Gain(MathF.Pow(10f, s.HeadroomDb / 20f)));

        switch (s.Preset)
        {
            case DspPresetKind.Headphones:
                _stages.Add(new Tilt(ch, rate, 0.04f));
                foreach (var (f, g, q) in DeviceEqBands(s.DeviceEqProfile)) _stages.Add(new PeakingEq(ch, rate, f, g, q));
                break;

            case DspPresetKind.Speakers:
                _stages.Add(new Tilt(ch, rate, -0.02f));
                if (ch >= 2) _stages.Add(new SpeakerSetup(ch, rate, s));
                break;

            case DspPresetKind.Crossfeed:
                if (ch >= 2) _stages.Add(new Crossfeed(ch, 0.35f));
                break;

            case DspPresetKind.RoomIr:
                _stages.Add(new Convolver(ch, ImpulseResponses.Load(s.ConvolutionIrPath)));
                break;

            case DspPresetKind.Parametric:
                foreach (var (f, g, q) in EasyEqBands(s.EasyEqJson)) _stages.Add(new PeakingEq(ch, rate, f, g, q));
                break;
        }

        _upsampler = upsample ? new HalfBandUpsampler(ch) : null;
    }

    private static IEnumerable<(float F, float G, float Q)> DeviceEqBands(string? profile)
        => profile?.ToLowerInvariant() switch
        {
            "harman" => [(100f, 2f, 0.7f), (3000f, 1.5f, 1.2f)],
            "bright" => [(8000f, 3f, 0.9f)],
            "warm" => [(200f, 2.5f, 0.8f)],
            _ => []
        };

    private static IEnumerable<(float F, float G, float Q)> EasyEqBands(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var bands = new List<(float, float, float)>();
            foreach (var band in doc.RootElement.EnumerateArray().Take(16))
            {
                bands.Add((Number(band, "f", 1000f), Number(band, "g", 0f), Math.Max(0.1f, Number(band, "q", 1f))));
            }

            return bands;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static float Number(JsonElement obj, string name, float fallback)
    {
        if (obj.ValueKind != JsonValueKind.Object) return fallback;
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.Number)
            {
                return prop.Value.GetSingle();
            }
        }

        return fallback;
    }

    private interface IStage
    {
        void Process(float[] x);
        void Reset();
    }

    private sealed class Gain(float gain) : IStage
    {
        public void Process(float[] x)
        {
            for (var i = 0; i < x.Length; i++) x[i] *= gain;
        }

        public void Reset() { }
    }

    /// <summary>저역을 살짝 더하거나 덜어 기울기를 준다. 코너는 샘플레이트와 무관하게 360Hz.</summary>
    private sealed class Tilt(int ch, int rate, float amount) : IStage
    {
        private readonly float[] _state = new float[ch];
        private readonly float _keep = MathF.Exp(-2f * MathF.PI * 360f / rate);

        public void Process(float[] x)
        {
            for (var i = 0; i < x.Length; i++)
            {
                var c = i % ch;
                _state[c] = _state[c] * _keep + x[i] * (1f - _keep);
                x[i] += _state[c] * amount;
            }
        }

        public void Reset() => Array.Clear(_state);
    }

    /// <summary>RBJ 피킹 EQ. 전치 직접형 II — 계수가 바뀌지 않는 한 수치적으로 가장 얌전하다.</summary>
    private sealed class PeakingEq : IStage
    {
        private readonly int _ch;
        private readonly float _b0, _b1, _b2, _a1, _a2;
        private readonly float[] _z1, _z2;

        public PeakingEq(int ch, int rate, float freq, float gainDb, float q)
        {
            _ch = ch;
            _z1 = new float[ch];
            _z2 = new float[ch];

            var a = MathF.Pow(10f, gainDb / 40f);
            var w0 = 2 * MathF.PI * Math.Clamp(freq, 20f, rate / 2f - 1f) / rate;
            var alpha = MathF.Sin(w0) / (2 * Math.Max(0.1f, q));
            var a0 = 1 + alpha / a;
            _b0 = (1 + alpha * a) / a0;
            _b1 = -2 * MathF.Cos(w0) / a0;
            _b2 = (1 - alpha * a) / a0;
            _a1 = -2 * MathF.Cos(w0) / a0;
            _a2 = (1 - alpha / a) / a0;
        }

        public void Process(float[] x)
        {
            for (var i = 0; i < x.Length; i++)
            {
                var c = i % _ch;
                var input = x[i];
                var output = _b0 * input + _z1[c];
                _z1[c] = _b1 * input - _a1 * output + _z2[c];
                _z2[c] = _b2 * input - _a2 * output;
                x[i] = output;
            }
        }

        public void Reset()
        {
            Array.Clear(_z1);
            Array.Clear(_z2);
        }
    }

    /// <summary>좌우 스피커의 거리·음량 차이 보정. 지연선이 조각 경계를 넘어 이어진다.</summary>
    private sealed class SpeakerSetup : IStage
    {
        private readonly int _ch;
        private readonly float[][] _line = new float[2][];
        private readonly int[] _at = new int[2];
        private readonly float[] _gain = new float[2];

        public SpeakerSetup(int ch, int rate, DspSettings s)
        {
            _ch = ch;
            _line[0] = new float[(int)Math.Clamp(s.SpeakerDelayMsLeft * rate / 1000f, 0, rate)];
            _line[1] = new float[(int)Math.Clamp(s.SpeakerDelayMsRight * rate / 1000f, 0, rate)];
            _gain[0] = MathF.Pow(10f, s.SpeakerGainLeftDb / 20f);
            _gain[1] = MathF.Pow(10f, s.SpeakerGainRightDb / 20f);
        }

        public void Process(float[] x)
        {
            for (var i = 0; i + 1 < x.Length; i += _ch)
            {
                for (var c = 0; c < 2; c++)
                {
                    var line = _line[c];
                    var value = x[i + c];
                    if (line.Length > 0)
                    {
                        var delayed = line[_at[c]];
                        line[_at[c]] = value;
                        _at[c] = (_at[c] + 1) % line.Length;
                        value = delayed;
                    }

                    x[i + c] = value * _gain[c];
                }
            }
        }

        public void Reset()
        {
            Array.Clear(_line[0]);
            Array.Clear(_line[1]);
            _at[0] = _at[1] = 0;
        }
    }

    private sealed class Crossfeed(int ch, float mix) : IStage
    {
        public void Process(float[] x)
        {
            for (var i = 0; i + 1 < x.Length; i += ch)
            {
                var l = x[i];
                var r = x[i + 1];
                x[i] = l * (1 - mix) + r * mix * 0.5f;
                x[i + 1] = r * (1 - mix) + l * mix * 0.5f;
            }
        }

        public void Reset() { }
    }

    /// <summary>임펄스 응답 컨볼루션. 앞 조각의 꼬리를 들고 있어 조각 경계에서 끊기지 않는다.</summary>
    private sealed class Convolver : IStage
    {
        private const int MaxTaps = 2048;
        private readonly int _ch;
        private readonly float[] _ir;
        private readonly float[][] _history;
        private readonly int[] _at;

        public Convolver(int ch, float[] ir)
        {
            _ch = ch;
            _ir = ir.Length > MaxTaps ? ir[..MaxTaps] : ir;
            _history = new float[ch][];
            for (var c = 0; c < ch; c++) _history[c] = new float[_ir.Length];
            _at = new int[ch];
        }

        public void Process(float[] x)
        {
            var taps = _ir.Length;
            for (var i = 0; i < x.Length; i++)
            {
                var c = i % _ch;
                var line = _history[c];
                var at = _at[c];
                line[at] = x[i];

                // line[at] 이 가장 새 샘플이고, 뒤로 갈수록 과거다.
                float acc = 0;
                var j = at;
                for (var k = 0; k < taps; k++)
                {
                    acc += line[j] * _ir[k];
                    if (--j < 0) j = taps - 1;
                }

                _at[c] = at + 1 == taps ? 0 : at + 1;
                x[i] = acc;
            }
        }

        public void Reset()
        {
            foreach (var line in _history) Array.Clear(line);
            Array.Clear(_at);
        }
    }
}

/// <summary>
/// 2배 업샘플러. 하프밴드 FIR(카이저 창, 95탭)의 다상 구현이다.
///
/// 샘플 사이를 직선으로 잇는 방식(예전 구현)은 필터가 아니다 — 원래 대역 위쪽에 거울상이
/// 그대로 남고 고역이 깎인다. 하프밴드 필터는 원본 샘플을 그대로 통과시키고(짝수 출력),
/// 그 사이 값만 주변 48개 샘플로 계산한다(홀수 출력). 거울상은 80dB 넘게 눌린다.
/// </summary>
public sealed class HalfBandUpsampler
{
    /// <summary>한쪽 탭 수. 지연은 입력 기준 이만큼의 샘플이다(44.1kHz 에서 0.54ms).</summary>
    public const int HalfTaps = 24;

    private static readonly float[] Odd = DesignOddPhase();

    private readonly int _ch;
    private readonly float[][] _history;
    private int _at;

    public HalfBandUpsampler(int channels)
    {
        _ch = Math.Max(1, channels);
        _history = new float[_ch][];
        for (var c = 0; c < _ch; c++) _history[c] = new float[HalfTaps * 2];
    }

    public void Reset()
    {
        foreach (var line in _history) Array.Clear(line);
        _at = 0;
    }

    /// <summary>입력 프레임마다 출력 프레임 둘. 지연은 내부 기억으로 흡수하므로 길이는 정확히 2배다.</summary>
    public float[] Process(float[] x)
    {
        var frames = x.Length / _ch;
        var y = new float[frames * 2 * _ch];
        var size = HalfTaps * 2;

        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < _ch; c++)
            {
                var line = _history[c];
                line[_at] = x[f * _ch + c];

                // 짝수 출력: HalfTaps 만큼 지난 원본 샘플 그대로.
                var center = _at - HalfTaps;
                if (center < 0) center += size;
                y[f * 2 * _ch + c] = line[center];

                // 홀수 출력: 그 샘플과 다음 샘플 사이. 가장 새 샘플부터 과거로 가며 곱한다.
                float acc = 0;
                var j = _at;
                for (var k = 0; k < size; k++)
                {
                    acc += line[j] * Odd[k];
                    if (--j < 0) j = size - 1;
                }

                y[(f * 2 + 1) * _ch + c] = acc;
            }

            _at = _at + 1 == size ? 0 : _at + 1;
        }

        return y;
    }

    /// <summary>
    /// 홀수 위상 계수. 이상적인 하프밴드의 홀수 탭은 sinc(m/2) 이고, 카이저 창(β=8)으로 다듬는다.
    /// k 번째 계수는 가장 새 샘플에서 k 만큼 과거의 입력에 곱해진다.
    /// </summary>
    private static float[] DesignOddPhase()
    {
        const double beta = 8.0;
        var size = HalfTaps * 2;
        var taps = new double[size];
        var span = 2.0 * HalfTaps - 1;   // 창이 덮는 탭 인덱스 범위: -(2J-1) .. +(2J-1)
        double sum = 0;

        for (var k = 0; k < size; k++)
        {
            // 홀수 탭 인덱스 m. 가운데 두 탭이 m = ±1 이다.
            var m = 2 * (k - HalfTaps) + 1;
            var arg = Math.PI * m / 2.0;
            var ideal = Math.Sin(arg) / arg;
            var ratio = m / span;
            var window = BesselI0(beta * Math.Sqrt(Math.Max(0, 1 - ratio * ratio))) / BesselI0(beta);
            taps[k] = ideal * window;
            sum += taps[k];
        }

        // DC 이득을 정확히 1 로. 창을 씌우면 합이 1 에서 아주 조금 벗어난다.
        var result = new float[size];
        for (var k = 0; k < size; k++) result[k] = (float)(taps[k] / sum);
        return result;
    }

    private static double BesselI0(double x)
    {
        double sum = 1, term = 1;
        var half = x / 2;
        for (var k = 1; k < 40; k++)
        {
            term *= half / k * (half / k);
            sum += term;
            if (term < sum * 1e-16) break;
        }

        return sum;
    }
}

/// <summary>룸 보정용 임펄스 응답 파일. 한 번 읽은 것은 다시 읽지 않는다.</summary>
public static class ImpulseResponses
{
    private static readonly float[] Fallback = [0.92f, 0.06f, 0.015f, 0.005f];
    private static readonly ConcurrentDictionary<string, float[]> Cache = new();

    public static float[] Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Fallback;

        return Cache.GetOrAdd(path, p =>
        {
            try
            {
                if (!p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var file = File.OpenRead(p);
                    return ReadWav(file);
                }

                using var zip = System.IO.Compression.ZipFile.OpenRead(p);
                var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
                if (entry is null) return Fallback;

                // 압축 스트림은 되감을 수 없다. 헤더 파서가 자리를 옮겨 다니므로 메모리에 풀어 준다.
                using var memory = new MemoryStream();
                using (var src = entry.Open()) src.CopyTo(memory);
                memory.Position = 0;
                return ReadWav(memory);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                return Fallback;
            }
        });
    }

    /// <summary>앞 2초를 모노로 섞어 최대 8192탭까지 읽는다.</summary>
    private static float[] ReadWav(Stream stream)
    {
        var header = WavHeader.Parse(stream);
        if (header is null || header.SourceBytesPerFrame <= 0) return Fallback;

        var frames = (int)Math.Min(header.DataSize / header.SourceBytesPerFrame, Math.Min(8192, header.SampleRate * 2L));
        if (frames <= 0) return Fallback;

        var raw = new byte[frames * header.SourceBytesPerFrame];
        stream.Position = header.DataPosition;
        var read = 0;
        while (read < raw.Length)
        {
            var n = stream.Read(raw, read, raw.Length - read);
            if (n <= 0) break;
            read += n;
        }

        if (read < raw.Length) raw = raw[..(read / header.SourceBytesPerFrame * header.SourceBytesPerFrame)];
        var pcm = PcmSamples.ToDeviceDepth(raw, header.SourceBits, header.Encoding, out var bits);
        var samples = PcmMath.ToFloat(pcm, bits);

        var ch = Math.Max(1, header.Channels);
        var mono = new float[samples.Length / ch];
        for (var i = 0; i < mono.Length; i++)
        {
            float s = 0;
            for (var c = 0; c < ch; c++) s += samples[i * ch + c];
            mono[i] = s / ch;
        }

        return mono.Length > 0 ? mono : Fallback;
    }
}
