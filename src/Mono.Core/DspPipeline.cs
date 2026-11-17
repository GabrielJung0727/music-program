using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// Core 쪽 DSP 입구. 계산 자체는 <see cref="DspChain"/> 이 한다 — 출력 워커가 클럭싱크로 파일을 직접
/// 열 때도 같은 체인을 쓰므로, 혼자 듣든 라운지든 같은 설정은 같은 소리가 난다.
/// Audiophile·DSP Off이면 입력을 그대로 반환(Bit-perfect).
/// </summary>
public static class DspPipeline
{
    [ThreadStatic] private static TpdfDither? t_dither;

    private static TpdfDither Dither => t_dither ??= new TpdfDither();

    /// <summary>상태를 이어 가지 않는 한 번짜리 호출. 스트림에는 체인을 넘기는 쪽을 쓴다.</summary>
    public static byte[] Process(
        byte[] pcm,
        int sampleRate,
        int bitDepth,
        int channels,
        ListeningRoom room,
        out int outRate,
        out int outDepth,
        out bool srcApplied)
        => Process(pcm, sampleRate, bitDepth, channels, room, null, out outRate, out outDepth, out srcApplied);

    /// <summary>
    /// 조각 하나를 가공한다. 같은 스트림의 조각은 같은 <paramref name="chain"/> 으로 넘겨야
    /// 필터가 조각 경계에서 끊기지 않는다.
    /// </summary>
    public static byte[] Process(
        byte[] pcm,
        int sampleRate,
        int bitDepth,
        int channels,
        ListeningRoom room,
        DspChain? chain,
        out int outRate,
        out int outDepth,
        out bool srcApplied)
    {
        outRate = sampleRate;
        outDepth = bitDepth;
        srcApplied = false;

        var settings = DspSettings.From(room);
        if (!settings.Active)
        {
            return pcm;
        }

        var samples = PcmMath.ToFloat(pcm, bitDepth);
        samples = (chain ?? new DspChain()).Process(samples, sampleRate, channels, settings, allowUpsample: true, out outRate);
        srcApplied = outRate != sampleRate;
        outDepth = 24;
        return PcmMath.FromFloat(samples, 24, Dither);
    }

    public static byte[] ApplyGain(byte[] pcm, int bitDepth, int volumePercent)
    {
        if (volumePercent >= 100 || pcm.Length == 0) return pcm;
        var gain = PcmGain.FromPercent(volumePercent);
        return PcmGain.Apply(pcm, bitDepth, 1, gain, gain, Dither);
    }

    public static float[] ToFloat(byte[] pcm, int bitDepth) => PcmMath.ToFloat(pcm, bitDepth);

    /// <summary>계산을 거치지 않은 신호를 되돌린다. <see cref="ToFloat"/> 의 정확한 역이다.</summary>
    public static byte[] FromFloat(float[] samples, int bitDepth) => PcmMath.FromFloatExact(samples, bitDepth);

    public static float[] LoadIr(string? path) => ImpulseResponses.Load(path);
}
