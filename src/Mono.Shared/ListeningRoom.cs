namespace Mono.Shared;

public sealed class ListeningRoom
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string HostPeerId { get; set; }
    public RoomMode Mode { get; set; } = RoomMode.OpenLounge;
    public PlaybackSourceMode SourceMode { get; set; } = PlaybackSourceMode.ClockSync;
    public QualityPolicy QualityPolicy { get; set; } = QualityPolicy.LowestCommonFormat;
    public DspPresetKind DspPreset { get; set; } = DspPresetKind.Off;
    public bool DspLocked { get; set; }
    public bool DspEnabled { get; set; }
    public bool SeekingAllowed { get; set; } = true;
    public bool CommentsAllowed { get; set; } = true;
    public bool ChatCollapsed { get; set; }
    public bool QueueLocked { get; set; }
    public bool FollowHostView { get; set; }
    public bool AutoAdvance { get; set; } = true;
    public int LinerPage { get; set; }
    public int MaxMembers { get; set; } = 16;
    public string? InviteCode { get; set; }
    public DateTimeOffset? InviteExpiresAt { get; set; }
    public int QueueIndex { get; set; }
    public bool Playing { get; set; }
    public long MediaOriginUnixMs { get; set; }
    public long MediaTimeAtOriginMs { get; set; }
    /// <summary>리싱크 세대. 값이 바뀌면 Output은 버퍼를 비우고 다시 락한다.</summary>
    public long ResyncEpoch { get; set; }
    public bool BitPerfect { get; set; } = true;
    public bool SrcApplied { get; set; }
    public string PathBadge { get; set; } = "Bit-perfect";
    public bool ArchiveDefaultConsent { get; set; }
    public int CommentRetentionDays { get; set; } = 90;
    public bool AnonymizeArchive { get; set; }
    public bool CloudSyncOptIn { get; set; }
    public byte[]? FanOutKey { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<QueueItem> Queue { get; } = [];
    public List<QueueRequest> Requests { get; } = [];
    public List<TimestampPin> Pins { get; } = [];
    public List<Reaction> Reactions { get; } = [];
    public List<ChatLine> Chat { get; } = [];
    public List<string> PlayedTrackIds { get; } = [];
    public HashSet<string> ControlPeerIds { get; } = [];
    public HashSet<string> OutputPeerIds { get; } = [];
    public HashSet<string> SpectatorPeerIds { get; } = [];
    public HashSet<string> BannedPeerIds { get; } = [];
    public Dictionary<string, string> PeerNames { get; } = [];
    public Dictionary<string, MemberRole> Roles { get; } = [];
    public Dictionary<string, PeerStats> Stats { get; } = [];
    public Dictionary<string, OutputCapability> Outputs { get; } = [];

    public static ListeningRoom ForMode(string id, string name, string hostPeerId, RoomMode mode)
    {
        var room = new ListeningRoom { Id = id, Name = name, HostPeerId = hostPeerId, Mode = mode };
        room.FanOutKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        switch (mode)
        {
            case RoomMode.Audiophile:
                room.DspEnabled = false;
                room.DspPreset = DspPresetKind.Off;
                room.DspLocked = true;
                room.SeekingAllowed = false;
                room.ChatCollapsed = true;
                room.SourceMode = PlaybackSourceMode.ClockSync;
                room.QualityPolicy = QualityPolicy.RequireBitPerfect;
                break;
            case RoomMode.HostQueue:
                room.SeekingAllowed = false;
                room.SourceMode = PlaybackSourceMode.FanOut;
                break;
            case RoomMode.Invite:
                room.InviteCode = Random.Shared.Next(100000, 999999).ToString();
                room.InviteExpiresAt = DateTimeOffset.UtcNow.AddHours(6);
                break;
            case RoomMode.OpenLounge:
                room.SourceMode = PlaybackSourceMode.FanOut;
                break;
        }

        room.ControlPeerIds.Add(hostPeerId);
        room.Roles[hostPeerId] = MemberRole.Host;
        return room;
    }

    public MemberRole RoleOf(string peerId)
    {
        if (peerId == HostPeerId)
        {
            return MemberRole.Host;
        }

        if (SpectatorPeerIds.Contains(peerId))
        {
            return MemberRole.Spectator;
        }

        return Roles.TryGetValue(peerId, out var role) ? role : MemberRole.Listener;
    }

    /// <summary>호스트 또는 위임받은 DJ인가. 공동 DJ 핸드오프의 판정점.</summary>
    public bool CanDirect(string peerId)
        => peerId == HostPeerId || RoleOf(peerId) == MemberRole.Dj;

    public Track? CurrentTrack(IReadOnlyDictionary<string, Track> catalog)
    {
        if (QueueIndex < 0 || QueueIndex >= Queue.Count)
        {
            return null;
        }

        return catalog.TryGetValue(Queue[QueueIndex].TrackId, out var track) ? track : null;
    }

    public long CurrentMediaTimeMs()
    {
        if (!Playing)
        {
            return MediaTimeAtOriginMs;
        }

        return MediaTimeAtOriginMs + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - MediaOriginUnixMs);
    }
}

public sealed record ChatLine(string PeerId, string Text, DateTimeOffset At);
