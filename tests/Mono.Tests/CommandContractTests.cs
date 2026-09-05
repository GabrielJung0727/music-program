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

    [Fact]
    public void PinCarriesMediaTimeAndText()
    {
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.Pin,
            MediaTimeMs = 42_000,
            Text = "이 구간"
        });
        Assert.Equal("pin", m.Type);
        Assert.Equal(42_000, m.MediaTimeMs);
        Assert.Equal("이 구간", m.Text);
    }

    [Fact]
    public void CreatePlaylistCarriesNameAndTrackIds()
    {
        // Core는 이름을 Text에서, 곡 목록을 TrackIds에서 읽는다.
        var m = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.CreatePlaylist,
            Text = "밤에 듣는 재즈",
            TrackIds = ["tr-blue-train", "tr-so-what"]
        });
        Assert.Equal("create_playlist", m.Type);
        Assert.Equal("밤에 듣는 재즈", m.Text);
        Assert.Equal(2, m.TrackIds!.Count);
        Assert.Contains("tr-so-what", m.TrackIds);
    }

    [Fact]
    public void PlaylistAndArchiveIdsSurviveRoundTrip()
    {
        var load = RoundTrip(new MonoMessage
        {
            Type = MessageTypes.LoadPlaylist,
            PlaylistId = "pl-1",
            Flag = true
        });
        Assert.Equal("load_playlist", load.Type);
        Assert.Equal("pl-1", load.PlaylistId);
        Assert.True(load.Flag);

        var share = RoundTrip(new MonoMessage { Type = MessageTypes.ShareSession, ArchiveId = "ar-1" });
        Assert.Equal("share_session", share.Type);
        Assert.Equal("ar-1", share.ArchiveId);
    }

    [Fact]
    public void ReactCarriesWhitelistedEmoji()
    {
        // Core가 화이트리스트를 강제하지만, 보내는 쪽도 같은 목록을 안다.
        foreach (var emoji in new[] { "❤️", "🎉", "👏", "🔥" })
        {
            var m = RoundTrip(new MonoMessage { Type = MessageTypes.React, Emoji = emoji });
            Assert.Equal(emoji, m.Emoji);
        }
    }
}
