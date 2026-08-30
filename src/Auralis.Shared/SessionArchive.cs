namespace Auralis.Shared;

public sealed class SessionArchive
{
    public required string Id { get; init; }
    public required string RoomId { get; init; }
    public required string RoomName { get; init; }
    public required DateTimeOffset EndedAt { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public RoomMode Mode { get; init; }
    public string? HostPeerId { get; init; }
    public List<string> TrackIds { get; init; } = [];
    public List<string> HighlightTrackIds { get; init; } = [];
    public List<TimestampPin> Pins { get; init; } = [];
    public List<Reaction> Reactions { get; init; } = [];
    public List<ChatLine> Chat { get; init; } = [];
    public List<string> Participants { get; init; } = [];
    public List<SegmentHit> Hits { get; init; } = [];
    public bool Anonymized { get; init; }
    /// <summary>룸의 코멘트 보존 정책. 지나면 핀·채팅이 정리된다.</summary>
    public int RetentionDays { get; init; } = 90;
}

public sealed class UserPlaylist
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public List<string> TrackIds { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? FromArchiveId { get; init; }
    public string? OwnerPeerId { get; init; }
}
