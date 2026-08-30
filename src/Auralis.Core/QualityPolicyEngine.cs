using Auralis.Shared;

namespace Auralis.Core;

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

        var nativeOk = track.IsDsd
            ? cap.SupportsDsd
            : cap.MaxSampleRate >= track.SampleRate && cap.MaxBitDepth >= track.BitDepth;

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

                var src = !nativeOk;
                return new Decision(true, false, src, Badge(track, src), null);
        }
    }

    public static string FormatOf(Track t)
        => t.IsDsd ? $"DSD{t.DsdRate ?? 64}" : $"{t.BitDepth}/{t.SampleRate}";

    public static string Badge(Track t, bool src)
    {
        var srcMark = src ? " · SRC" : " · Bit-perfect";
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
