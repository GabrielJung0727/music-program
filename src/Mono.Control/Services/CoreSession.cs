using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Mono.Protocol;

namespace Mono.Control.Services;

/// <summary>Core :7700 TCP 세션. Control은 오디오를 재생하지 않는다.</summary>
public sealed class CoreSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = LineFraming.JsonOptions;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public string PeerId { get; } = "ctl-" + Guid.NewGuid().ToString("n")[..8];
    public string DisplayName { get; set; } = Environment.UserName;
    public bool IsConnected { get; private set; }
    public string? LastError { get; private set; }

    public event Action<MonoMessage>? MessageReceived;
    public event Action? ConnectionChanged;

    public async Task<bool> ConnectAsync(string host = "127.0.0.1", int port = 7700, CancellationToken ct = default)
    {
        await DisconnectAsync();
        try
        {
            _client = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(8));
            await _client.ConnectAsync(host, port, linked.Token);
            _stream = _client.GetStream();
            _reader = new StreamReader(_stream, Encoding.UTF8);
            _cts = new CancellationTokenSource();
            IsConnected = true;
            LastError = null;
            ConnectionChanged?.Invoke();

            await SendAsync(new MonoMessage
            {
                Type = MessageTypes.Hello,
                PeerId = PeerId,
                DisplayName = DisplayName,
                Role = "control"
            });

            _ = Task.Run(() => ReadLoopAsync(_cts.Token));
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            IsConnected = false;
            ConnectionChanged?.Invoke();
            return false;
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader is not null)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line is null) break;
                var msg = JsonSerializer.Deserialize<MonoMessage>(line, JsonOpts);
                if (msg is not null) MessageReceived?.Invoke(msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsConnected = false;
            ConnectionChanged?.Invoke();
        }
    }

    public async Task SendAsync(MonoMessage message)
    {
        if (_stream is null || !IsConnected) throw new InvalidOperationException("Core에 연결되어 있지 않습니다.");
        message.PeerId ??= PeerId;
        await _sendGate.WaitAsync();
        try
        {
            var bytes = LineFraming.Encode(message);
            await _stream.WriteAsync(bytes);
            await _stream.FlushAsync();
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public Task PlayAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Play });
    public Task PauseAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Pause });
    public Task SkipAsync(int delta = 1) => SendAsync(new MonoMessage { Type = MessageTypes.Skip, Index = delta });
    public Task SeekAsync(long ms) => SendAsync(new MonoMessage { Type = MessageTypes.Seek, MediaTimeMs = ms });
    public Task EnqueueAsync(string trackId) => SendAsync(new MonoMessage { Type = MessageTypes.Enqueue, TrackId = trackId });
    public Task CatalogAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Catalog });
    public Task SearchAsync(string q) => SendAsync(new MonoMessage { Type = MessageTypes.Search, Text = q });
    public Task ScanAsync(string? path) => SendAsync(new MonoMessage { Type = MessageTypes.ScanLibrary, Path = path });
    public Task ListRoomsAsync() => SendAsync(new MonoMessage { Type = MessageTypes.ListRooms });
    public Task CreateRoomAsync(string name, int mode) => SendAsync(new MonoMessage { Type = MessageTypes.CreateRoom, RoomName = name, Mode = (Mono.Shared.RoomMode)mode });
    public Task JoinRoomAsync(string roomId, string? invite = null) => SendAsync(new MonoMessage { Type = MessageTypes.JoinRoom, RoomId = roomId, InviteCode = invite });
    public Task LeaveRoomAsync() => SendAsync(new MonoMessage { Type = MessageTypes.LeaveRoom });
    public Task ReactAsync(string emoji) => SendAsync(new MonoMessage { Type = MessageTypes.React, Emoji = emoji });
    public Task ChatAsync(string text) => SendAsync(new MonoMessage { Type = MessageTypes.Chat, Text = text });
    public Task EndpointsAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Endpoints });
    public Task ListZonesAsync() => SendAsync(new MonoMessage { Type = MessageTypes.ListZones });
    public Task CreateZoneAsync(string name) => SendAsync(new MonoMessage { Type = MessageTypes.CreateZone, Text = name });
    public Task LinkStreamingAsync(int provider) => SendAsync(new MonoMessage { Type = MessageTypes.LinkStreaming, Provider = (Mono.Shared.StreamingProvider)provider, Token = "demo-token" });
    public Task HistoryAsync() => SendAsync(new MonoMessage { Type = MessageTypes.History });
    public Task PlaylistsAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Playlists });
    public Task ArchivesAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Archives });
    public Task SetDspAsync(int preset) => SendAsync(new MonoMessage { Type = MessageTypes.SetDsp, Dsp = (Mono.Shared.DspPresetKind)preset });
    public Task ResyncAsync() => SendAsync(new MonoMessage { Type = MessageTypes.Resync });
    public Task ChooseAutoplayAsync(string trackId) => SendAsync(new MonoMessage { Type = MessageTypes.ChooseAutoplay, TrackId = trackId });
    public Task WikiAsync(string artistId) => SendAsync(new MonoMessage { Type = MessageTypes.WikiBio, Text = artistId });
    public Task ClearQueueAsync() => SendAsync(new MonoMessage { Type = MessageTypes.ClearQueue });
    public Task EndSessionAsync(bool consent) => SendAsync(new MonoMessage { Type = MessageTypes.EndSession, Consent = consent });

    public async Task DisconnectAsync()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _reader?.Dispose();
        _reader = null;
        if (_stream is not null) await _stream.DisposeAsync();
        _stream = null;
        _client?.Dispose();
        _client = null;
        IsConnected = false;
        ConnectionChanged?.Invoke();
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
