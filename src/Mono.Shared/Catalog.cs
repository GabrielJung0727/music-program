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

    /// <summary>파일 태그의 장르. Genres 화면과 장르 타일 집계에 쓴다.</summary>
    public List<string> Genres { get; init; } = [];

    /// <summary>파일 태그의 작곡가. Composers·Compositions 화면의 근거다.</summary>
    public List<string> Composers { get; init; } = [];
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
    /// <summary>다국어 검색용 별칭 — 원어 표기, 로마자, 다른 언어권 표기 등 (예: 요네즈 켄시 ↔ 米津玄師).</summary>
    public List<string> AlternateNames { get; init; } = [];
}

/// <summary>위키백과에서 가져온 아티스트 이력 요약 — 저작권상 짧은 발췌 + 출처만 보관한다.</summary>
public sealed record ArtistWikiSummary(
    string ArtistId,
    string Title,
    string Extract,
    string SourceUrl,
    string? ThumbnailUrl,
    string Lang,
    DateTimeOffset FetchedAt);

/// <summary>
/// 멀티 디바이스 존 — 한 사용자가 보유한 여러 출력기기를 묶어 싱글 플레이로 관리한다.
/// Sync면 존의 모든 기기가 같은 방(룸)에 출력되고, Independent면 기기마다 별도 개인 방을 갖는다.
/// </summary>
public sealed class Zone
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string OwnerPeerId { get; init; }
    public ZoneMode Mode { get; set; } = ZoneMode.Sync;
    public List<string> MemberPeerIds { get; init; } = [];
    /// <summary>Sync 모드에서 존 전원이 출력되는 공유 개인 방.</summary>
    public string? SyncRoomId { get; set; }
    /// <summary>Independent 모드에서 기기별로 배정된 개인 방.</summary>
    public Dictionary<string, string> IndependentRoomIds { get; init; } = [];
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

    /// <summary>출력 장치 상태 머신의 현재 값. 0=Idle 2=ExclusiveStreaming 4=DeviceBusyLocked 6=DeviceLostSuspend.</summary>
    public int DeviceState { get; set; }

    /// <summary>재생을 보류한 사유. 점유 충돌·장치 분리처럼 사용자가 조치해야 하는 것만 담는다.</summary>
    public string? DeviceError { get; set; }

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
