using System.Text.Json;
using Mono.Protocol;
using Microsoft.AspNetCore.SignalR;

namespace Mono.Core;

/// <summary>
/// 웹 Control의 컨트롤 플레인. 오디오는 이 허브를 지나가지 않는다(소셜/오디오 격리).
/// </summary>
public sealed class LoungeHub : Hub
{
    private readonly CommandProcessor _commands;
    private readonly RoomBroadcaster _broadcaster;
    private readonly ConnectionRegistry _connections;
    private readonly RoomManager _rooms;
    private readonly IHubContext<LoungeHub> _hub;

    public LoungeHub(
        CommandProcessor commands,
        RoomBroadcaster broadcaster,
        ConnectionRegistry connections,
        RoomManager rooms,
        IHubContext<LoungeHub> hub)
    {
        _commands = commands;
        _broadcaster = broadcaster;
        _connections = connections;
        _rooms = rooms;
        _hub = hub;
    }

    public override Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();

        // 이 허브에는 페어링 절차가 없다. 이 PC 에서 온 연결만 받는다.
        if (!RequestGuard.IsLoopback(http?.Connection.RemoteIpAddress))
        {
            Context.Abort();
            return Task.CompletedTask;
        }

        // 쿼리로 받은 id 도 hello 와 같은 규칙을 탄다. 주인이 있는 id 는 비밀값 없이 내주지 않는다.
        var requested = http?.Request.Query["peer"].ToString();
        var peer = _connections.Claim(
            string.IsNullOrWhiteSpace(requested) ? "web-" + Context.ConnectionId[..8] : requested,
            http?.Request.Query["secret"].ToString()).PeerId;

        Context.Items["peer"] = peer;
        Register(peer, Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items["peer"] is string peer)
        {
            _connections.Controls.TryRemove(peer, out _);

            // 창을 닫거나 새로고침하면 leave_room 은 오지 않는다. 여기서 빼 주지 않으면
            // 끊긴 사람이 멤버로 남아 방이 영영 비지 않는다.
            foreach (var room in _rooms.DetachControl(peer))
            {
                await _broadcaster.PublishAsync(room);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task Send(MonoMessage message)
    {
        var peer = Context.Items["peer"] as string ?? Context.ConnectionId;
        var name = Context.GetHttpContext()?.Request.Query["name"].ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = message.DisplayName;
        }

        if (message.Type == MessageTypes.Hello)
        {
            string? secret = null;
            if (!string.IsNullOrWhiteSpace(message.PeerId) && message.PeerId != peer)
            {
                _connections.Controls.TryRemove(peer, out _);
                (peer, secret) = _connections.Claim(message.PeerId, message.Secret);
            }

            Context.Items["peer"] = peer;
            Register(peer, Context.ConnectionId);
            await Clients.Caller.SendAsync("msg", new MonoMessage { Type = MessageTypes.Welcome, PeerId = peer, Secret = secret, Ok = true });
            await Clients.Caller.SendAsync("msg", _commands.CatalogMessage());
            var rooms = _commands.Execute(peer, new MonoMessage { Type = MessageTypes.ListRooms }, name);
            if (rooms.Direct is not null)
            {
                await Clients.Caller.SendAsync("msg", rooms.Direct);
            }

            return;
        }

        var result = _commands.Execute(peer, message, name);
        if (result.Direct is not null)
        {
            await Clients.Caller.SendAsync("msg", result.Direct);
        }

        if (result.BroadcastAll is not null)
        {
            await _broadcaster.PushCatalogAsync(result.BroadcastAll);
        }

        if (result.BroadcastRoom is not null)
        {
            await _broadcaster.PublishAsync(result.BroadcastRoom);
        }
        else if (message.Type is MessageTypes.EndSession or MessageTypes.LeaveRoom)
        {
            await Clients.Caller.SendAsync("msg", new MonoMessage
            {
                Type = MessageTypes.ListRooms,
                Body = JsonSerializer.Serialize(_rooms.List().Select(r => new { r.Id, r.Name, r.Mode }), LineFraming.JsonOptions)
            });
        }
    }

    private void Register(string peer, string connectionId)
        => _connections.Controls[peer] = msg => _hub.Clients.Client(connectionId).SendAsync("msg", msg);
}
