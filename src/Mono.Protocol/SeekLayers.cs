namespace Mono.Protocol;

/// <summary>시크바 위 반응 히트맵 구간. 좌표와 세기 모두 0–1이다.</summary>
public readonly record struct HeatBand(double Start, double End, double Intensity);

/// <summary>시크바 위 타임스탬프 핀. Position은 0–1이다.</summary>
public readonly record struct PinMark(double Position, string Id, string Text, string? PeerName);

/// <summary>
/// Core가 보낸 히트맵·핀을 시크바 좌표로 바꾼다.
/// Core는 10초 버킷으로 집계하므로(RoomManager.ReactionHeatmap) 여기서도 같은 폭을 쓴다.
/// </summary>
public static class SeekLayers
{
    /// <summary>Core의 히트맵 버킷 폭. RoomManager.ReactionHeatmap과 같아야 한다.</summary>
    public const long BucketMs = 10_000;

    public static IReadOnlyList<HeatBand> Bands(IEnumerable<SnapshotHeatBucket> buckets, long durationMs)
    {
        if (durationMs <= 0) return [];
        var list = buckets.Where(b => b.Count > 0).ToList();
        if (list.Count == 0) return [];

        var busiest = (double)list.Max(b => b.Count);
        return list
            .Select(b => new HeatBand(
                Math.Clamp(b.BucketMs / (double)durationMs, 0, 1),
                Math.Clamp((b.BucketMs + BucketMs) / (double)durationMs, 0, 1),
                b.Count / busiest))
            .ToList();
    }

    public static IReadOnlyList<PinMark> Marks(IEnumerable<SnapshotPin> pins, long durationMs)
    {
        if (durationMs <= 0) return [];
        return pins
            .Where(p => p.OnCurrentTrack)
            .Select(p => new PinMark(
                Math.Clamp(p.MediaTimeMs / (double)durationMs, 0, 1),
                p.Id,
                p.Text,
                p.PeerName))
            .ToList();
    }
}
