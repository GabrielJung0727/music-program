using Mono.Shared;

namespace Mono.Control.Models;

/// <summary>라운지 참가자 한 명.</summary>
public sealed record MemberEntry(
    string PeerId, string Name, MemberRole Role, bool IsOutput, bool Spectator, string SyncText)
{
    public string RoleLabel => Role switch
    {
        MemberRole.Host => "호스트",
        MemberRole.Dj => "DJ",
        MemberRole.Spectator => "참관",
        _ => "청취자"
    };

    public string Detail => string.Join(" · ", new[]
    {
        RoleLabel,
        IsOutput ? "출력" : null,
        Spectator ? "참관 중" : null,
        string.IsNullOrEmpty(SyncText) ? null : SyncText
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Host Queue 모드에서 게스트가 올린 선곡 요청.</summary>
public sealed record RequestEntry(string Id, string Title, string FromName);

/// <summary>타임스탬프 핀 하나.</summary>
public sealed record PinEntry(string Id, string Text, string PeerName, long MediaTimeMs, bool OnCurrentTrack)
{
    public string At => TimeSpan.FromMilliseconds(MediaTimeMs).ToString(@"m\:ss");
    public string Detail => $"{PeerName} · {At}";
}
