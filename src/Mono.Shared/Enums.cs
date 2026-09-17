namespace Mono.Shared;

public enum RoomMode
{
    OpenLounge,
    Invite,
    HostQueue,
    Audiophile,

    /// <summary>
    /// 혼자 듣기. Core 에서는 혼자 듣는 것도 룸이라 큐·타임라인·출력이 매달릴 자리가 필요하지만,
    /// 이건 라운지가 아니다 — 라운지 목록에 뜨지 않고 아무도 들어올 수 없다.
    ///
    /// 값을 4 로 붙인 건 앞의 넷을 밀지 않기 위해서다. 이 숫자는 그대로 와이어에 실린다.
    /// 예전에 웹 쪽이 Solo 를 2 로 알고 보내는 바람에 혼자 듣기가 HostQueue 룸으로 만들어졌고,
    /// 곡을 틀 때마다 공개 라운지가 하나씩 생겼다. 새 값을 넣을 때는 항상 뒤에 붙인다.
    /// </summary>
    Solo = 4
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
    Crossfeed,
    Parametric
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
