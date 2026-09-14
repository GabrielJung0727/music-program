using Mono.Protocol;
using Mono.Shared;

namespace Mono.Control.Services;

/// <summary>
/// 멀티 디바이스 존 — 싱글 플레이 전용. Sync면 존 전원이 같은 개인 방에,
/// Independent면 기기마다 별도 개인 방에 배정된다. 실제 방 재배정은 Core가 한다.
/// </summary>
public sealed partial class CoreSession
{
    public Task RenameZoneAsync(string zoneId, string name)
        => SendAsync(new MonoMessage { Type = MessageTypes.RenameZone, ZoneId = zoneId, Text = name });

    public Task SetZoneModeAsync(string zoneId, ZoneMode mode)
        => SendAsync(new MonoMessage { Type = MessageTypes.SetZoneMode, ZoneId = zoneId, ZoneMode = mode });

    public Task ZoneAddMemberAsync(string zoneId, string peerId)
        => SendAsync(new MonoMessage { Type = MessageTypes.ZoneAddMember, ZoneId = zoneId, TargetPeerId = peerId });

    public Task ZoneRemoveMemberAsync(string zoneId, string peerId)
        => SendAsync(new MonoMessage { Type = MessageTypes.ZoneRemoveMember, ZoneId = zoneId, TargetPeerId = peerId });

    public Task DeleteZoneAsync(string zoneId)
        => SendAsync(new MonoMessage { Type = MessageTypes.DeleteZone, ZoneId = zoneId });
}
