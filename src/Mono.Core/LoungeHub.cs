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
        var peer = Context.GetHttpContext()?.Request.Query["peer"].ToString();
        if (string.IsNullOrWhiteSpace(peer))
        {
            peer = "web-" + Context.ConnectionId[..8];
        }

        Context.Items["peer"] = peer;
        Register(peer, Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items["peer"] is string peer)
        {
            _connections.Controls.TryRemove(peer, out _);
        }

        return base.OnDisconnectedAsync(exception);
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
            peer = string.IsNullOrWhiteSpace(message.PeerId) ? peer : message.PeerId;
            Context.Items["peer"] = peer;
            Register(peer, Context.ConnectionId);
            await Clients.Caller.SendAsync("msg", new MonoMessage { Type = MessageTypes.Welcome, PeerId = peer, Ok = true });
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
