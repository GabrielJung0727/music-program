namespace Mono.Shared;

public sealed class Track
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public required string AlbumId { get; set; }
    public required string ArtistId { get; set; }
    public string? LocalPath { get; set; }
    public string? StreamingId { get; set; }
    public StreamingProvider Source { get; set; } = StreamingProvider.Local;
    public StreamingQuality StreamingQuality { get; set; } = StreamingQuality.Unknown;
    public int SampleRate { get; set; } = 44100;
    public int BitDepth { get; set; } = 16;
    public int Channels { get; set; } = 2;
    public bool IsDsd { get; set; }
    public int? DsdRate { get; set; }
    public long DurationMs { get; set; }
    public string? LyricsLrc { get; set; }
    public string? ArtworkPath { get; set; }
    public bool MergedLocalAndStreaming { get; set; }
    public int TrackNumber { get; set; }
}

public sealed class Album
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public required string ArtistId { get; set; }
    public string? LinerNotes { get; set; }
    public string? Label { get; set; }
    public int? Year { get; set; }
    public string? Credits { get; set; }
    public string? ArtworkPath { get; set; }
}

public sealed class Artist
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public List<string> RelatedArtistIds { get; init; } = [];
    public string? Bio { get; set; }
}

public sealed class QueueItem
{
    public required string Id { get; init; }
    public required string TrackId { get; init; }
    public required string AddedByPeerId { get; init; }
}

public sealed class QueueRequest
{
    public required string Id { get; init; }
    public required string TrackId { get; init; }
    public required string FromPeerId { get; init; }
}

public sealed record TimestampPin(
    string Id,
    string PeerId,
    string TrackId,
    long MediaTimeMs,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record Reaction(
    string PeerId,
    string TrackId,
    string Emoji,
    DateTimeOffset At,
    long MediaTimeMs = 0);

public sealed record LyricsLine(long TimeMs, string Text);

/// <summary>구간 히트 — 핀/반응이 몰린 재생 구간(버킷 단위).</summary>
public sealed record SegmentHit(string TrackId, long BucketMs, int Count);

public sealed class OutputCapability
{
    public required string PeerId { get; init; }
    public required string DisplayName { get; set; }
    public int MaxSampleRate { get; set; } = 192000;
    public int MaxBitDepth { get; set; } = 32;
    public bool SupportsDsd { get; set; }
    public bool ExclusiveMode { get; set; } = true;
    public long ReportedLatencyMs { get; set; } = 5;
    public bool HardwareVolume { get; set; }
    public int VolumePercent { get; set; } = 100;
    public string? Device { get; set; }
}

/// <summary>Output이 보고하는 동기화 품질. 측정 UI(오프셋·지터)의 원천.</summary>
public sealed class PeerStats
{
    public double OffsetMs { get; set; }
    public double JitterMs { get; set; }
    public double RttMs { get; set; }
    public int BufferMs { get; set; }
    public int Resyncs { get; set; }
    public bool Locked { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Core가 기억하는 출력 엔드포인트. 오프라인이어도 목록에 남는다.</summary>
public sealed class EndpointRecord
{
    public required string PeerId { get; init; }
    public required string DisplayName { get; set; }
    public int MaxSampleRate { get; set; } = 192000;
    public int MaxBitDepth { get; set; } = 32;
    public bool SupportsDsd { get; set; }
    public bool ExclusiveMode { get; set; } = true;
    public long LatencyMs { get; set; } = 5;
    public bool HardwareVolume { get; set; }
    public int VolumePercent { get; set; } = 100;
    public string? Device { get; set; }
    public string? RoomId { get; set; }
    public bool Online { get; set; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ListeningHistoryEntry
{
    public required string Id { get; init; }
    public required string PeerId { get; init; }
    public required string TrackId { get; init; }
    public string? RoomId { get; init; }
    public string? RoomName { get; init; }
    public DateTimeOffset HeardAt { get; init; }
    public bool Completed { get; set; }
}

public sealed class PairingToken
{
    public required string Code { get; init; }
    public required string PeerId { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class StreamingAccount
{
    public required StreamingProvider Provider { get; init; }
    public required string Token { get; set; }
    public bool Connected { get; set; }
    public string? DisplayName { get; set; }
}
