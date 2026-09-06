using Mono.Protocol;
using Mono.Shared;

namespace Mono.Control.Services;

/// <summary>룸 운영 제어 — 호스트만 유효하다. 권한 판정은 Core가 하고 실패는 error로 돌아온다.</summary>
public sealed partial class CoreSession
{
    public Task InviteAsync(InviteAction action, int? minutes = null)
        => SendAsync(new MonoMessage { Type = MessageTypes.Invite, InviteAction = action, Minutes = minutes });

    public Task KickAsync(string targetPeerId)
        => SendAsync(new MonoMessage { Type = MessageTypes.Kick, TargetPeerId = targetPeerId });

    public Task TransferHostAsync(string targetPeerId)
        => SendAsync(new MonoMessage { Type = MessageTypes.TransferHost, TargetPeerId = targetPeerId });

    public Task SetRoleAsync(string targetPeerId, MemberRole role)
        => SendAsync(new MonoMessage { Type = MessageTypes.SetRole, TargetPeerId = targetPeerId, Member = role });

    public Task SpectateAsync(bool on)
        => SendAsync(new MonoMessage { Type = MessageTypes.Spectate, Flag = on });

    public Task SetPolicyAsync(QualityPolicy policy)
        => SendAsync(new MonoMessage { Type = MessageTypes.SetPolicy, Policy = policy });

    public Task SetSourceModeAsync(PlaybackSourceMode mode)
        => SendAsync(new MonoMessage { Type = MessageTypes.SetSourceMode, SourceMode = mode });

    /// <summary>
    /// 룸 플래그 토글. flag 이름은 Core CommandProcessor가 받는 값 그대로다:
    /// seek · comments · chat · queue_lock · follow_host · auto_advance ·
    /// smart_autoplay · cloud · dsp_lock · max · name
    /// </summary>
    public Task SetRoomFlagAsync(string flag, bool? value = null, int? index = null, string? roomName = null)
        => SendAsync(new MonoMessage
        {
            Type = MessageTypes.SetRoomFlags,
            Text = flag,
            Flag = value,
            Index = index,
            RoomName = roomName
        });
}
