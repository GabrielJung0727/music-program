using Mono.Protocol;

namespace Mono.Control.Services;

/// <summary>라운지 생성·참여·큐·채팅·존. 권한 판정은 Core가 하고 실패는 error로 돌아온다.</summary>
public sealed partial class CoreSession
{
    public Task ListRoomsAsync() => SendAsync(new MonoMessage { Type = MessageTypes.ListRooms });
    public Task CreateRoomAsync(string name, int mode) => SendAsync(new MonoMessage { Type = MessageTypes.CreateRoom, RoomName = name, Mode = (Mono.Shared.RoomMode)mode });
    public Task JoinRoomAsync(string roomId, string? invite = null) => SendAsync(new MonoMessage { Type = MessageTypes.JoinRoom, RoomId = roomId, InviteCode = invite });
    public Task LeaveRoomAsync() => SendAsync(new MonoMessage { Type = MessageTypes.LeaveRoom });
    public Task EnqueueAsync(string trackId) => SendAsync(new MonoMessage { Type = MessageTypes.Enqueue, TrackId = trackId });
    public Task ClearQueueAsync() => SendAsync(new MonoMessage { Type = MessageTypes.ClearQueue });
    public Task ReactAsync(string emoji) => SendAsync(new MonoMessage { Type = MessageTypes.React, Emoji = emoji });
    public Task ChatAsync(string text) => SendAsync(new MonoMessage { Type = MessageTypes.Chat, Text = text });
    public Task EndSessionAsync(bool consent) => SendAsync(new MonoMessage { Type = MessageTypes.EndSession, Consent = consent });
    public Task LinerPageAsync(int page) => SendAsync(new MonoMessage { Type = MessageTypes.LinerPage, Index = page });
    public Task LinerScrollAsync(double y) => SendAsync(new MonoMessage { Type = MessageTypes.LinerScroll, OffsetMs = y });
    public Task FollowHostAsync(bool on) => SendAsync(new MonoMessage { Type = MessageTypes.FollowHost, Flag = on });
    public Task ListZonesAsync() => SendAsync(new MonoMessage { Type = MessageTypes.ListZones });
    public Task CreateZoneAsync(string name) => SendAsync(new MonoMessage { Type = MessageTypes.CreateZone, Text = name });
}
