using System.Text.Json;
using Mono.Shared;

namespace Mono.Protocol;

/// <summary>
/// Core <see cref="MessageTypes.RoomState"/> 페이로드의 타입 계약.
/// Control이 손파싱 대신 이걸 쓴다. 모르는 필드는 무시해 Core 선행 배포를 견딘다.
/// </summary>
public sealed class RoomSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public RoomMode Mode { get; set; }
    public PlaybackSourceMode SourceMode { get; set; }
    public QualityPolicy QualityPolicy { get; set; }

    // DSP
    public DspPresetKind DspPreset { get; set; }
    public bool DspEnabled { get; set; }
    public bool DspLocked { get; set; }
    public string? ConvolutionIrPath { get; set; }
    public string? EasyEqJson { get; set; }
    public bool EasyEqGraphicMode { get; set; }
    public double HeadroomDb { get; set; }
    public double SpeakerDelayMsLeft { get; set; }
    public double SpeakerDelayMsRight { get; set; }
    public double SpeakerGainLeftDb { get; set; }
    public double SpeakerGainRightDb { get; set; }
    public string? DeviceEqProfile { get; set; }

    // 정책 플래그
    public bool SeekingAllowed { get; set; }
    public bool CommentsAllowed { get; set; }
    public bool ChatCollapsed { get; set; }
    public bool QueueLocked { get; set; }
    public bool FollowHostView { get; set; }
    public bool AutoAdvance { get; set; }
    public bool SmartAutoplay { get; set; }
    public int LinerPage { get; set; }
    public double LinerScrollY { get; set; }
    public int MaxMembers { get; set; }
    public string? InviteCode { get; set; }
    public DateTimeOffset? InviteExpiresAt { get; set; }

    // 아카이브 정책
    public bool ArchiveDefaultConsent { get; set; }
    public int CommentRetentionDays { get; set; }
    public bool AnonymizeArchive { get; set; }
    public bool CloudSyncOptIn { get; set; }

    // 재생 타임라인
    public bool Playing { get; set; }
    public int QueueIndex { get; set; }
    public string? HostPeerId { get; set; }
    public long ResyncEpoch { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public long MediaTimeMs { get; set; }
    public long DurationMs { get; set; }
    public long MediaOriginUnixMs { get; set; }
    public long MediaTimeAtOriginMs { get; set; }

    // 신뢰 UI
    public bool BitPerfect { get; set; }
    public bool SrcApplied { get; set; }
    public string? PathBadge { get; set; }

    public int CatalogCount { get; set; }

    // 컬렉션은 절대 null이 아니다 — UI가 null 검사 없이 순회한다.
    public List<SnapshotQueueItem> Queue { get; set; } = [];
    public List<SnapshotRequest> Requests { get; set; } = [];
    public List<SnapshotPin> Pins { get; set; } = [];
    public List<SnapshotReaction> Reactions { get; set; } = [];
    public Dictionary<string, int> ReactionCounts { get; set; } = [];
    public List<string> AllowedReactionEmoji { get; set; } = [];
    public List<SnapshotHeatBucket> Heatmap { get; set; } = [];
    public List<SnapshotChatLine> Chat { get; set; } = [];
    public List<SnapshotMember> Members { get; set; } = [];
    public List<SnapshotOutput> Outputs { get; set; } = [];
    public List<string> Spectators { get; set; } = [];
    public Dictionary<string, string> Peers { get; set; } = [];

    // 현재 재생 맥락 — 곡이 없으면 CurrentTrack·Album·Artist·Autoplay가 전부 null이다.
    public SnapshotTrack? CurrentTrack { get; set; }
    public SnapshotAlbum? Album { get; set; }
    public SnapshotArtist? Artist { get; set; }
    public List<SnapshotTrack> AlbumTracks { get; set; } = [];
    public List<SnapshotArtist> RelatedArtists { get; set; } = [];
    public string? LinerNotes { get; set; }
    public string? Credits { get; set; }
    public List<SnapshotLyricLine> Lyrics { get; set; } = [];
    public string? CurrentLyric { get; set; }
    public SnapshotAutoplay? Autoplay { get; set; }

    /// <summary>깨진 JSON이면 null. 호출자가 화면을 유지할 수 있게 예외를 던지지 않는다.</summary>
    public static RoomSnapshot? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<RoomSnapshot>(json, LineFraming.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class SnapshotQueueItem
{
    public string Id { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string AddedByPeerId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Artist { get; set; }
    public long DurationMs { get; set; }
    public string? Badge { get; set; }
    public string? ArtUrl { get; set; }
}

public sealed class SnapshotRequest
{
    public string Id { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string FromPeerId { get; set; } = "";
    public string? FromName { get; set; }
    public string? Title { get; set; }
}

public sealed class SnapshotPin
{
    public string Id { get; set; } = "";
    public string PeerId { get; set; } = "";
    public string? PeerName { get; set; }
    public string TrackId { get; set; } = "";
    public long MediaTimeMs { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public bool OnCurrentTrack { get; set; }
}

public sealed class SnapshotReaction
{
    public string PeerId { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string Emoji { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public long MediaTimeMs { get; set; }
}

/// <summary>반응 히트맵의 10초 버킷.</summary>
public sealed class SnapshotHeatBucket
{
    public string TrackId { get; set; } = "";
    public long BucketMs { get; set; }
    public int Count { get; set; }
}

public sealed class SnapshotChatLine
{
    public string PeerId { get; set; } = "";
    public string? PeerName { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class SnapshotMember
{
    public string PeerId { get; set; } = "";
    public string? Name { get; set; }
    public MemberRole Role { get; set; }
    public bool IsOutput { get; set; }
    public bool Spectator { get; set; }
    public PeerStats? Stats { get; set; }
}

public sealed class SnapshotOutput
{
    public string PeerId { get; set; } = "";
    public string? DisplayName { get; set; }
    public int MaxSampleRate { get; set; }
    public int MaxBitDepth { get; set; }
    public bool SupportsDsd { get; set; }
    public bool ExclusiveMode { get; set; }
    public long ReportedLatencyMs { get; set; }
    public bool HardwareVolume { get; set; }
    public int VolumePercent { get; set; }
    public string? Device { get; set; }
    public bool Spectator { get; set; }
    public string? Badge { get; set; }
    public string? Note { get; set; }
    public PeerStats? Stats { get; set; }
}

public sealed class SnapshotTrack
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? AlbumId { get; set; }
    public string? ArtistId { get; set; }
    public string? ArtistName { get; set; }
    public string? AlbumTitle { get; set; }
    public int SampleRate { get; set; }
    public int BitDepth { get; set; }
    public int Channels { get; set; }
    public bool IsDsd { get; set; }
    public int? DsdRate { get; set; }
    public long DurationMs { get; set; }
    public StreamingProvider Source { get; set; }
    public StreamingQuality StreamingQuality { get; set; }
    public bool MergedLocalAndStreaming { get; set; }
    public bool HasLocal { get; set; }
    public string? Badge { get; set; }
    public string? ArtUrl { get; set; }
}

public sealed class SnapshotAlbum
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ArtistId { get; set; }
    public string? LinerNotes { get; set; }
    public string? Label { get; set; }
    public int? Year { get; set; }
    public string? Credits { get; set; }
    public string? ArtworkPath { get; set; }
}

public sealed class SnapshotArtist
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> RelatedArtistIds { get; set; } = [];
    public string? Bio { get; set; }
    public List<string> AlternateNames { get; set; } = [];
}

public sealed class SnapshotLyricLine
{
    public long TimeMs { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>곡 종료 약 40초 전 제시되는 스마트 오토플레이 후보.</summary>
public sealed class SnapshotAutoplay
{
    public List<SnapshotTrack> Candidates { get; set; } = [];
    public long DeadlineUnixMs { get; set; }
    public string? ChosenId { get; set; }
}
