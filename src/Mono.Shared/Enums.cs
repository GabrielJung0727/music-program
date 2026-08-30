namespace Mono.Shared;

public enum RoomMode
{
    OpenLounge,
    Invite,
    HostQueue,
    Audiophile
}

public enum PlaybackSourceMode
{
    ClockSync,
    FanOut
}

public enum PeerRole
{
    Control,
    Output
}

/// <summary>
/// 룸 안에서의 권한 역할. Invite 모드의 "권한 역할"과 참관을 함께 표현한다.
/// </summary>
public enum MemberRole
{
    Host,
    Dj,
    Listener,
    Spectator
}

public enum InviteAction
{
    Rotate,
    Extend,
    Revoke
}

public enum QualityPolicy
{
    RequireBitPerfect,
    LowestCommonFormat,
    SpectatorIfIncompatible
}

public enum DspPresetKind
{
    Off,
    Headphones,
    Speakers,
    RoomIr,
    Crossfeed
}

public enum StreamingProvider
{
    Local,
    Tidal,
    Qobuz
}

public enum StreamingQuality
{
    Unknown,
    HiFi,
    Max,
    Studio
}
