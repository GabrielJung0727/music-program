using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 룸 품질 정책 판정. "전원 Bit-perfect일 때만", "최저 공통 포맷", "불가 시 참관"의 세 축.
/// </summary>
public static class QualityPolicyEngine
{
    public readonly record struct Decision(bool CanHear, bool Spectator, bool NeedSrc, string Badge, string? Error);

    public static Decision Evaluate(ListeningRoom room, Track track, OutputCapability? cap)
    {
        if (cap is null)
        {
            return new Decision(false, true, false, "Control only", null);
        }

        var nativeOk = NativeOk(track, cap);

        switch (room.QualityPolicy)
        {
            case QualityPolicy.RequireBitPerfect:
                return nativeOk
                    ? new Decision(true, false, false, Badge(track, src: false), null)
                    : new Decision(false, true, false, "Incompatible",
                        $"이 룸은 네이티브 고정입니다. 기기가 {FormatOf(track)}를 지원하지 않습니다.");

            case QualityPolicy.SpectatorIfIncompatible:
                return nativeOk
                    ? new Decision(true, false, false, Badge(track, false), null)
                    : new Decision(false, true, false, "Spectator",
                        $"지원 불가 — 참관 모드. 필요 포맷: {FormatOf(track)}");

            default:
                if (track.IsDsd && !cap.SupportsDsd)
                {
                    return new Decision(false, true, false, "Spectator", "DSD를 PCM으로 내리지 않고 참관합니다.");
                }

                // 규격이 맞지 않아도 내보내지는 않는다. 다만 Mono 가 규격을 맞춰 주지는 않는다 —
                // 리샘플러가 없다. 예전에는 여기서 "SRC" 라고 표시했는데, 하지 않는 변환이었다.
                // 실제로 무슨 일이 생기는지를 적는다.
                return nativeOk
                    ? new Decision(true, false, false, Badge(track, src: false), null)
                    : new Decision(true, false, false, Badge(track, src: false, bitPerfect: false),
                        $"이 장치는 {FormatOf(track)} 를 그대로 받지 못합니다. Mono 는 규격을 바꿔 주지 않습니다. "
                        + "배타 모드와 ASIO 에서는 재생되지 않고, 공유 모드로 내려가면 Windows 가 변환합니다.");
        }
    }

    /// <summary>이 장치가 곡의 규격을 그대로 받는가. 샘플레이트는 장치가 보고한 목록이 있으면 그것으로 본다.</summary>
    public static bool NativeOk(Track track, OutputCapability cap)
        => track.IsDsd
            ? cap.SupportsDsd
            : cap.AcceptsRate(track.SampleRate) && cap.MaxBitDepth >= track.BitDepth;

    public static string FormatOf(Track t)
        => t.IsDsd ? $"DSD{t.DsdRate ?? 64}" : $"{t.BitDepth}/{t.SampleRate}";

    /// <summary>
    /// 경로 배지. "Bit-perfect" 는 실제로 그럴 때만 붙인다 — 변환도 없고 비트퍼펙트도 아니면(DSP,
    /// 공유 모드, 디지털 볼륨, 장치가 받지 못하는 규격) 아무 표시도 붙이지 않는다.
    /// </summary>
    public static string Badge(Track t, bool src, bool bitPerfect = true)
    {
        var srcMark = src ? " · SRC" : bitPerfect ? " · Bit-perfect" : "";
        var stream = t.Source == StreamingProvider.Local
            ? (t.MergedLocalAndStreaming ? " Local+Stream" : " Local")
            : $" {t.Source} {t.StreamingQuality}";
        return FormatOf(t) + srcMark + stream;
    }

    /// <summary>참여 중인 모든 Output이 함께 소화할 수 있는 최저 공통 포맷.</summary>
    public static (int Rate, int Depth) CommonFormat(IEnumerable<OutputCapability> caps, Track track)
    {
        var list = caps.ToList();
        if (list.Count == 0)
        {
            return (track.SampleRate, track.BitDepth);
        }

        var rate = Math.Min(track.SampleRate, list.Min(c => c.MaxSampleRate));
        var depth = Math.Min(track.BitDepth, list.Min(c => c.MaxBitDepth));
        return (rate, depth);
    }

    /// <summary>
    /// 볼륨 적용 방식. Audiophile/Bit-perfect 룸에서는 하드웨어 볼륨만 허용한다.
    /// </summary>
    public static bool AllowsDigitalVolume(ListeningRoom room, OutputCapability cap)
        => !cap.HardwareVolume
           && room.Mode != RoomMode.Audiophile
           && room.QualityPolicy != QualityPolicy.RequireBitPerfect;
}
