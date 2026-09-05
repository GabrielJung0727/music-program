using System.Text.Json;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// Control이 보내는 명령이 Core가 읽는 필드에 실리는지 고정한다.
/// 타입이 맞아도 필드가 비면 Core는 조용히 무시한다 — 그걸 여기서 잡는다.
/// </summary>
public class CommandContractTests
{
    private static MonoMessage RoundTrip(MonoMessage msg)
    {
        var json = JsonSerializer.Serialize(msg, LineFraming.JsonOptions);
        return JsonSerializer.Deserialize<MonoMessage>(json, LineFraming.JsonOptions)!;
    }

    [Fact]
    public void InviteCarriesActionAndMinutes()
    {
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.Invite,
            InviteAction = InviteAction.Extend,
            Minutes = 30
        });
        Assert.Equal("invite", m.Type);
        Assert.Equal(InviteAction.Extend, m.InviteAction);
        Assert.Equal(30, m.Minutes);
    }

    [Fact]
    public void SetRoleCarriesTargetAndRole()
    {
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.SetRole,
            TargetPeerId = "guest-1",
            Member = MemberRole.Dj
        });
        Assert.Equal("set_role", m.Type);
        Assert.Equal("guest-1", m.TargetPeerId);
        Assert.Equal(MemberRole.Dj, m.Member);
    }

    [Fact]
    public void MoveQueueCarriesIndexAndDelta()
    {
        var m = RoundTrip(new MonoMessage { Type = MessageTypes.MoveQueue, Index = 3, Delta = -1 });
        Assert.Equal("move_queue", m.Type);
        Assert.Equal(3, m.Index);
        Assert.Equal(-1, m.Delta);
    }

    [Fact]
    public void SetRoomFlagCarriesNameAndValue()
    {
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.SetRoomFlags,
            Text = "queue_lock",
            Flag = true
        });
        Assert.Equal("set_room_flags", m.Type);
        Assert.Equal("queue_lock", m.Text);
        Assert.True(m.Flag);
    }

    [Fact]
    public void SetPolicyAndSourceModeCarryEnums()
    {
        var p = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.SetPolicy,
            Policy = QualityPolicy.LowestCommonFormat
        });
        Assert.Equal(QualityPolicy.LowestCommonFormat, p.Policy);

        var s = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.SetSourceMode,
            SourceMode = PlaybackSourceMode.FanOut
        });
        Assert.Equal(PlaybackSourceMode.FanOut, s.SourceMode);
    }
}
