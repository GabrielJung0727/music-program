using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mono.Control.Models;
using Mono.Control.Services;
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Control.ViewModels.Pages;

/// <summary>
/// 라운지 — 룸 생성·참여·공동 큐·채팅·반응.
/// 큐와 채팅은 Core 스냅샷이 단일 진실이므로 여기서 임의로 바꾸지 않는다.
/// </summary>
public sealed partial class LoungeViewModel : PageViewModel
{
    public LoungeViewModel(CoreSession session) : base(session) { }

    public ObservableCollection<RoomListItem> Rooms { get; } = new();
    public ObservableCollection<string> ChatLines { get; } = new();
    public ObservableCollection<CatalogTrack> QueueTracks { get; } = new();

    [ObservableProperty] private string _roomName = "Night Lounge";
    [ObservableProperty] private int _roomMode;
    [ObservableProperty] private string _joinRoomId = "";
    [ObservableProperty] private string _inviteCode = "";
    [ObservableProperty] private string _chatInput = "";

    /// <summary>내 peerId. 호스트 여부 판정에 쓴다. 셸이 생성 직후 넣어 준다.</summary>
    public string SelfPeerId { get; set; } = "";

    public ObservableCollection<MemberEntry> Members { get; } = new();
    public ObservableCollection<RequestEntry> Requests { get; } = new();
    public ObservableCollection<PinEntry> Pins { get; } = new();

    [ObservableProperty] private bool _isHost;
    [ObservableProperty] private bool _inRoom;
    [ObservableProperty] private string _inviteCodeText = "초대 코드 없음";
    [ObservableProperty] private string _inviteExpiryText = "";
    [ObservableProperty] private bool _seekingAllowed = true;
    [ObservableProperty] private bool _commentsAllowed = true;
    [ObservableProperty] private bool _queueLocked;
    [ObservableProperty] private bool _autoAdvance = true;
    [ObservableProperty] private bool _smartAutoplay = true;
    [ObservableProperty] private bool _dspLocked;
    [ObservableProperty] private int _maxMembers = 16;
    [ObservableProperty] private int _qualityPolicyIndex;
    [ObservableProperty] private int _sourceModeIndex;
    [ObservableProperty] private string _pinText = "";
    [ObservableProperty] private long _currentMediaMs;
    [ObservableProperty] private string _currentArtistId = "";

    /// <summary>호스트가 아닐 때 왜 못 바꾸는지. 버튼을 숨기지 않고 사유를 붙인다.</summary>
    public string HostBlockedReason => IsHost ? "" : "호스트만 바꿀 수 있습니다";

    partial void OnIsHostChanged(bool value) => OnPropertyChanged(nameof(HostBlockedReason));

    public override void ApplySnapshot(RoomSnapshot snapshot)
    {
        QueueTracks.Clear();
        foreach (var q in snapshot.Queue)
        {
            QueueTracks.Add(new CatalogTrack
            {
                Id = q.TrackId,
                Title = q.Title,
                Artist = q.Artist,
                DurationMs = q.DurationMs,
                Badge = q.Badge,
                ArtUrl = q.ArtUrl
            });
        }

        ChatLines.Clear();
        foreach (var c in snapshot.Chat) ChatLines.Add($"{c.PeerName ?? c.PeerId}: {c.Text}");

        InRoom = !string.IsNullOrEmpty(snapshot.Id);
        IsHost = !string.IsNullOrEmpty(SelfPeerId) && snapshot.HostPeerId == SelfPeerId;
        CurrentMediaMs = snapshot.MediaTimeMs;

        Members.Clear();
        foreach (var m in snapshot.Members)
        {
            var sync = m.Stats is { } st ? $"{st.OffsetMs:+0.0;-0.0}ms" : "";
            Members.Add(new MemberEntry(m.PeerId, m.Name ?? m.PeerId, m.Role, m.IsOutput, m.Spectator, sync));
        }

        Requests.Clear();
        foreach (var r in snapshot.Requests)
            Requests.Add(new RequestEntry(r.Id, r.Title ?? r.TrackId, r.FromName ?? r.FromPeerId));

        Pins.Clear();
        foreach (var p in snapshot.Pins)
            Pins.Add(new PinEntry(p.Id, p.Text, p.PeerName ?? p.PeerId, p.MediaTimeMs, p.OnCurrentTrack));

        InviteCodeText = string.IsNullOrEmpty(snapshot.InviteCode) ? "초대 코드 없음" : snapshot.InviteCode!;
        InviteExpiryText = snapshot.InviteExpiresAt is { } exp ? $"{exp.ToLocalTime():HH:mm} 까지" : "";

        SeekingAllowed = snapshot.SeekingAllowed;
        CommentsAllowed = snapshot.CommentsAllowed;
        QueueLocked = snapshot.QueueLocked;
        AutoAdvance = snapshot.AutoAdvance;
        SmartAutoplay = snapshot.SmartAutoplay;
        DspLocked = snapshot.DspLocked;
        MaxMembers = snapshot.MaxMembers;
        QualityPolicyIndex = (int)snapshot.QualityPolicy;
        SourceModeIndex = (int)snapshot.SourceMode;
    }

    public void ApplyRoomList(IEnumerable<RoomListItem> rooms)
    {
        Rooms.Clear();
        foreach (var r in rooms) Rooms.Add(r);
    }

    [RelayCommand]
    private Task CreateRoomAsync() => Safe(() => Session.CreateRoomAsync(RoomName, RoomMode));

    [RelayCommand]
    private Task JoinRoomAsync()
    {
        if (string.IsNullOrWhiteSpace(JoinRoomId)) return Task.CompletedTask;
        return Safe(() => Session.JoinRoomAsync(
            JoinRoomId.Trim(),
            string.IsNullOrWhiteSpace(InviteCode) ? null : InviteCode.Trim()));
    }

    [RelayCommand]
    private Task JoinListedRoomAsync(RoomListItem? room)
    {
        if (room is null) return Task.CompletedTask;
        JoinRoomId = room.Id;
        return JoinRoomAsync();
    }

    [RelayCommand]
    private Task LeaveRoomAsync() => Safe(() => Session.LeaveRoomAsync());

    [RelayCommand]
    private Task RefreshRoomsAsync() => Safe(() => Session.ListRoomsAsync());

    [RelayCommand]
    private Task ClearQueueAsync() => Safe(() => Session.ClearQueueAsync());

    [RelayCommand]
    private Task SendChatAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return Task.CompletedTask;
        var t = ChatInput.Trim();
        ChatInput = "";
        return Safe(() => Session.ChatAsync(t));
    }

    /// <summary>❤️🎉👏🔥 만 허용된다. Core가 화이트리스트와 곡당 3회 제한을 강제한다.</summary>
    [RelayCommand]
    private Task ReactAsync(string? emoji)
        => string.IsNullOrWhiteSpace(emoji) ? Task.CompletedTask : Safe(() => Session.ReactAsync(emoji));

    // ── 초대 ────────────────────────────────────────────
    [RelayCommand]
    private Task RotateInviteAsync() => Safe(() => Session.InviteAsync(InviteAction.Rotate, 360));

    [RelayCommand]
    private Task ExtendInviteAsync() => Safe(() => Session.InviteAsync(InviteAction.Extend, 60));

    [RelayCommand]
    private Task RevokeInviteAsync() => Safe(() => Session.InviteAsync(InviteAction.Revoke));

    // ── 멤버 ────────────────────────────────────────────
    [RelayCommand]
    private Task KickAsync(MemberEntry? m)
        => m is null ? Task.CompletedTask : Safe(() => Session.KickAsync(m.PeerId));

    [RelayCommand]
    private Task MakeHostAsync(MemberEntry? m)
        => m is null ? Task.CompletedTask : Safe(() => Session.TransferHostAsync(m.PeerId));

    [RelayCommand]
    private Task MakeDjAsync(MemberEntry? m)
        => m is null ? Task.CompletedTask : Safe(() => Session.SetRoleAsync(m.PeerId, MemberRole.Dj));

    [RelayCommand]
    private Task MakeListenerAsync(MemberEntry? m)
        => m is null ? Task.CompletedTask : Safe(() => Session.SetRoleAsync(m.PeerId, MemberRole.Listener));

    /// <summary>참관으로 전환하면 오디오를 받지 않는다 — 포맷이 안 맞을 때 쓴다.</summary>
    [RelayCommand]
    private Task ToggleSpectateAsync(string? on) => Safe(() => Session.SpectateAsync(on == "1"));

    // ── 정책 ────────────────────────────────────────────
    /// <summary>flag 이름은 Core CommandProcessor 가 받는 값 그대로다.</summary>
    [RelayCommand]
    private Task SetFlagAsync(string? flag)
        => string.IsNullOrEmpty(flag) ? Task.CompletedTask : Safe(() => Session.SetRoomFlagAsync(flag));

    [RelayCommand]
    private Task ApplyPolicyAsync() => Safe(() => Session.SetPolicyAsync((QualityPolicy)QualityPolicyIndex));

    [RelayCommand]
    private Task ApplySourceModeAsync() => Safe(() => Session.SetSourceModeAsync((PlaybackSourceMode)SourceModeIndex));

    [RelayCommand]
    private Task ApplyMaxMembersAsync() => Safe(() => Session.SetRoomFlagAsync("max", index: MaxMembers));

    // ── 큐 협업 ─────────────────────────────────────────
    [RelayCommand]
    private Task ApproveAsync(RequestEntry? r)
        => r is null ? Task.CompletedTask : Safe(() => Session.ApproveRequestAsync(r.Id));

    [RelayCommand]
    private Task RejectAsync(RequestEntry? r)
        => r is null ? Task.CompletedTask : Safe(() => Session.RejectRequestAsync(r.Id));

    [RelayCommand]
    private Task MoveUpAsync(CatalogTrack? t) => MoveAsync(t, -1);

    [RelayCommand]
    private Task MoveDownAsync(CatalogTrack? t) => MoveAsync(t, 1);

    private Task MoveAsync(CatalogTrack? track, int delta)
    {
        var i = track is null ? -1 : QueueTracks.IndexOf(track);
        return i < 0 ? Task.CompletedTask : Safe(() => Session.MoveQueueAsync(i, delta));
    }

    [RelayCommand]
    private Task RemoveFromQueueAsync(CatalogTrack? track)
    {
        var i = track is null ? -1 : QueueTracks.IndexOf(track);
        return i < 0 ? Task.CompletedTask : Safe(() => Session.RemoveQueueAsync(i));
    }

    [RelayCommand]
    private Task JumpToAsync(CatalogTrack? track)
    {
        var i = track is null ? -1 : QueueTracks.IndexOf(track);
        return i < 0 ? Task.CompletedTask : Safe(() => Session.JumpToAsync(i));
    }

    // ── 핀 ──────────────────────────────────────────────
    [RelayCommand]
    private Task AddPinAsync()
    {
        var text = PinText.Trim();
        if (text.Length == 0) return Task.CompletedTask;
        PinText = "";
        return Safe(() => Session.PinAsync(CurrentMediaMs, text));
    }

    [RelayCommand]
    private Task RemovePinAsync(PinEntry? pin)
        => pin is null ? Task.CompletedTask : Safe(() => Session.RemovePinAsync(pin.Id));

    /// <summary>핀 위치로 이동. 시킹이 잠긴 룸이면 Core 가 사유를 돌려준다.</summary>
    [RelayCommand]
    private Task SeekPinAsync(PinEntry? pin)
        => pin is null ? Task.CompletedTask : Safe(() => Session.SeekPinAsync(pin.Id));
}
