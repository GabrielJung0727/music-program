using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mono.Control.Models;
using Mono.Control.Services;
using Mono.Protocol;

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
}
