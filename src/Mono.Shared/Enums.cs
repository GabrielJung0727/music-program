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

/// <summary>멀티 디바이스 존의 재생 방식 — 싱글 플레이 전용.</summary>
public enum ZoneMode
{
    /// <summary>존에 속한 모든 기기가 같은 음원을 동시 재생한다.</summary>
    Sync,
    /// <summary>기기마다 각자 다른 음원을 독립적으로 재생한다.</summary>
    Independent
}
