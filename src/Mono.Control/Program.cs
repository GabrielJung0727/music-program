using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Mono.Protocol;
using Mono.Shared;

// Mono Control — 명령과 시각화만. 오디오 콜백을 돌리지 않는다.
// 그래픽 UI는 Core가 서빙하는 http://127.0.0.1:7702, 이 프로세스는 같은 프로토콜의 CLI다.

var host = Arg("--host=") ?? "127.0.0.1";
var uiUrl = Arg("--ui=") ?? $"http://{host}:7702";
var pairingToken = Arg("--token=");
var peerId = Arg("--peer=") ?? "ctl-" + Guid.NewGuid().ToString("n")[..6];
const int port = 7700;

if (!args.Contains("--no-ui"))
{
    try
    {
        Process.Start(new ProcessStartInfo(uiUrl) { UseShellExecute = true });
        Console.WriteLine($"Control UI: {uiUrl}  (오디오는 재생하지 않습니다)");
    }
    catch (Exception)
    {
        Console.WriteLine($"Control UI: {uiUrl}");
    }
}

using var client = new TcpClient();
await client.ConnectAsync(host, port);
var stream = client.GetStream();
var reader = new StreamReader(stream, Encoding.UTF8);

await Send(new MonoMessage
{
    Type = MessageTypes.Hello,
    PeerId = peerId,
    DisplayName = Environment.UserName,
    Role = "control",
    Token = pairingToken
});

_ = Task.Run(async () =>
{
    while (await reader.ReadLineAsync() is { } line)
    {
        var msg = JsonSerializer.Deserialize<MonoMessage>(line, LineFraming.JsonOptions);
        if (msg is null) continue;
        if (msg.Type == MessageTypes.RoomState)
        {
            PrintRoom(msg);
            continue;
        }

        Console.WriteLine(msg.Type
            + (msg.Error is not null ? "  ⚠ " + msg.Error : "")
            + (msg.PairingCode is not null ? "  code=" + msg.PairingCode : "")
            + (msg.Body is not null ? "\n" + Trunc(msg.Body) : ""));
    }
});

Help();
while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null || line.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
    var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (p.Length == 0) continue;
    try
    {
        if (p[0] is "help" or "?")
        {
            Help();
            continue;
        }

        MonoMessage? m = p[0].ToLowerInvariant() switch
        {
            // 룸
            "create" => new() { Type = MessageTypes.CreateRoom, Mode = ParseMode(Rest(p, 1) ?? "open"), RoomName = p.Length > 2 ? string.Join(' ', p.Skip(2)) : "Hi-Fi Lounge" },
            "join" => new() { Type = MessageTypes.JoinRoom, RoomId = p[1], InviteCode = p.ElementAtOrDefault(2) },
            "leave" => new() { Type = MessageTypes.LeaveRoom },
            "rooms" => new() { Type = MessageTypes.ListRooms },
            "invite" => new() { Type = MessageTypes.Invite, InviteAction = ParseInvite(Rest(p, 1) ?? "rotate"), Minutes = int.TryParse(p.ElementAtOrDefault(2), out var mins) ? mins : null },
            "kick" => new() { Type = MessageTypes.Kick, TargetPeerId = p[1] },
            "host" => new() { Type = MessageTypes.TransferHost, TargetPeerId = p[1] },
            "role" => new() { Type = MessageTypes.SetRole, TargetPeerId = p[1], Member = Enum.Parse<MemberRole>(p[2], true) },
            "spectate" => new() { Type = MessageTypes.Spectate, Flag = p.ElementAtOrDefault(1) != "off" },

            // 큐
            "add" => new() { Type = MessageTypes.Enqueue, TrackId = p[1] },
            "request" => new() { Type = MessageTypes.RequestTrack, TrackId = p[1] },
            "approve" => new() { Type = MessageTypes.ApproveRequest, Text = p[1] },
            "reject" => new() { Type = MessageTypes.RejectRequest, Text = p[1] },
            "rm" => new() { Type = MessageTypes.RemoveQueue, Index = int.Parse(p[1]) },
            "move" => new() { Type = MessageTypes.MoveQueue, Index = int.Parse(p[1]), Delta = int.Parse(p[2]) },
            "clear" => new() { Type = MessageTypes.ClearQueue },
            "jump" => new() { Type = MessageTypes.JumpTo, Index = int.Parse(p[1]) },

            // 트랜스포트
            "play" => new() { Type = MessageTypes.Play },
            "pause" => new() { Type = MessageTypes.Pause },
            "skip" => new() { Type = MessageTypes.Skip, Index = p.ElementAtOrDefault(1) == "prev" ? -1 : 1 },
            "seek" => new() { Type = MessageTypes.Seek, MediaTimeMs = long.Parse(p[1]) },
            "seekpin" => new() { Type = MessageTypes.SeekPin, Text = p[1] },
            "resync" => new() { Type = MessageTypes.Resync },

            // 소셜
            "pin" => new() { Type = MessageTypes.Pin, MediaTimeMs = long.Parse(p[1]), Text = string.Join(' ', p.Skip(2)) },
            "unpin" => new() { Type = MessageTypes.RemovePin, Text = p[1] },
            "chat" => new() { Type = MessageTypes.Chat, Text = string.Join(' ', p.Skip(1)) },
            "heart" => new() { Type = MessageTypes.React, Emoji = "♥" },
            "react" => new() { Type = MessageTypes.React, Emoji = p.ElementAtOrDefault(1) ?? "♥" },
            "follow" => new() { Type = MessageTypes.FollowArtist, Text = p[1] },
            "followhost" => new() { Type = MessageTypes.FollowHost, Flag = p.ElementAtOrDefault(1) != "off" },
            "liner" => new() { Type = MessageTypes.LinerPage, Index = int.Parse(p.ElementAtOrDefault(1) ?? "0") },

            // 호스트 설정
            "dsp" => new() { Type = MessageTypes.SetDsp, Dsp = Enum.Parse<DspPresetKind>(p[1], true) },
            "policy" => new() { Type = MessageTypes.SetPolicy, Policy = Enum.Parse<QualityPolicy>(p[1], true) },
            "retention" => new() { Type = MessageTypes.SetPolicy, Minutes = int.Parse(p[1]) },
            "anon" => new() { Type = MessageTypes.SetPolicy, Flag = p.ElementAtOrDefault(1) != "off" },
            "source" => new() { Type = MessageTypes.SetSourceMode, SourceMode = Enum.Parse<PlaybackSourceMode>(p[1], true) },
            "flag" => new() { Type = MessageTypes.SetRoomFlags, Text = p[1], Flag = p.ElementAtOrDefault(2) != "off", Index = int.TryParse(p.ElementAtOrDefault(2), out var idx) ? idx : null },
            "vol" => new() { Type = MessageTypes.SetVolume, TargetPeerId = p.Length > 2 ? p[1] : null, Volume = int.Parse(p[^1]) },

            // 라이브러리 · 개인 라이브러리
            "catalog" => new() { Type = MessageTypes.Catalog },
            "search" => new() { Type = MessageTypes.Search, Text = string.Join(' ', p.Skip(1)) },
            "graph" => new() { Type = MessageTypes.Graph, Text = p[1] },
            "scan" => new() { Type = MessageTypes.ScanLibrary, Path = p.ElementAtOrDefault(1) },
            "tidal" => new() { Type = MessageTypes.LinkStreaming, Provider = StreamingProvider.Tidal, Token = p.ElementAtOrDefault(1) ?? "demo-token" },
            "qobuz" => new() { Type = MessageTypes.LinkStreaming, Provider = StreamingProvider.Qobuz, Token = p.ElementAtOrDefault(1) ?? "demo-token" },
            "history" => new() { Type = MessageTypes.History },
            "archives" => new() { Type = MessageTypes.Archives },
            "playlists" => new() { Type = MessageTypes.Playlists },
            "makelist" => new() { Type = MessageTypes.CreatePlaylist, ArchiveId = p.ElementAtOrDefault(1) },
            "load" => new() { Type = MessageTypes.LoadPlaylist, PlaylistId = p.ElementAtOrDefault(1), Flag = p.Contains("replace") },
            "m3u" => new() { Type = MessageTypes.ExportM3u, PlaylistId = p.ElementAtOrDefault(1) },
            "share" => new() { Type = MessageTypes.ShareSession, ArchiveId = p.ElementAtOrDefault(1) },

            // 운영
            "endpoints" => new() { Type = MessageTypes.Endpoints },
            "pair" => new() { Type = MessageTypes.Pair },
            "redeem" => new() { Type = MessageTypes.Redeem, PairingCode = p[1] },
            "end" => new() { Type = MessageTypes.EndSession, Consent = p.ElementAtOrDefault(1) == "yes" },
            _ => null
        };
        if (m is null) Console.WriteLine("unknown command — help");
        else await Send(m);
    }
    catch (Exception ex)
    {
        Console.WriteLine("⚠ " + ex.Message);
    }
}

async Task Send(MonoMessage message) => await stream.WriteAsync(LineFraming.Encode(message));

static void PrintRoom(MonoMessage msg)
{
    try
    {
        using var doc = JsonDocument.Parse(msg.Body ?? "{}");
        var r = doc.RootElement;
        var track = r.TryGetProperty("currentTrack", out var t) && t.ValueKind == JsonValueKind.Object
            ? t.GetProperty("title").GetString()
            : "-";
        var media = r.GetProperty("mediaTimeMs").GetInt64();
        var duration = r.GetProperty("durationMs").GetInt64();
        var queue = r.GetProperty("queue").GetArrayLength();
        var outputs = r.GetProperty("outputs").GetArrayLength();
        Console.WriteLine(
            $"[{r.GetProperty("name").GetString()} · {r.GetProperty("mode")} · {r.GetProperty("pathBadge").GetString()}] " +
            $"{(r.GetProperty("playing").GetBoolean() ? "▶" : "⏸")} {track} " +
            $"{TimeSpan.FromMilliseconds(media):mm\\:ss}/{TimeSpan.FromMilliseconds(duration):mm\\:ss} " +
            $"queue={queue} outputs={outputs} room={r.GetProperty("id").GetString()}");

        if (r.TryGetProperty("currentLyric", out var lyric) && lyric.ValueKind == JsonValueKind.String)
        {
            Console.WriteLine("   ♪ " + lyric.GetString());
        }

        foreach (var o in r.GetProperty("outputs").EnumerateArray())
        {
            var stats = o.TryGetProperty("stats", out var s) && s.ValueKind == JsonValueKind.Object
                ? $"offset={s.GetProperty("offsetMs").GetDouble():F1}ms jitter={s.GetProperty("jitterMs").GetDouble():F1}ms buffer={s.GetProperty("bufferMs").GetInt32()}ms"
                : "측정 대기";
            Console.WriteLine($"   ▸ {o.GetProperty("displayName").GetString()} {o.GetProperty("badge").GetString()} vol={o.GetProperty("volumePercent").GetInt32()}% {stats}");
        }
    }
    catch (Exception)
    {
        Console.WriteLine("room_state");
    }
}

static string Trunc(string s) => s.Length < 600 ? s : s[..600] + "…";

static string? Rest(string[] p, int index) => p.ElementAtOrDefault(index);

static RoomMode ParseMode(string raw) => raw.ToLowerInvariant() switch
{
    "open" => RoomMode.OpenLounge,
    "invite" => RoomMode.Invite,
    "host" => RoomMode.HostQueue,
    "audiophile" => RoomMode.Audiophile,
    _ => RoomMode.OpenLounge
};

static InviteAction ParseInvite(string raw) => raw.ToLowerInvariant() switch
{
    "extend" => InviteAction.Extend,
    "revoke" => InviteAction.Revoke,
    _ => InviteAction.Rotate
};

static void Help() => Console.WriteLine("""
    ── 룸        create <open|invite|host|audiophile> <이름> · join <룸ID> [코드] · leave · rooms
                 invite <rotate|extend|revoke> [분] · kick <peer> · host <peer> · role <peer> <dj|listener|spectator> · spectate [off]
    ── 큐        add <트랙ID> · request <트랙ID> · approve <요청ID> · reject <요청ID> · rm <n> · move <n> <±d> · clear · jump <n>
    ── 재생      play · pause · skip [prev] · seek <ms> · seekpin <핀ID> · resync
    ── 소셜      pin <ms> <메모> · unpin <핀ID> · chat <말> · heart · react <이모지> · follow <아티스트ID> · followhost [off] · liner <n>
    ── 호스트    dsp <off|headphones|speakers|roomir|crossfeed> · policy <requirebitperfect|lowestcommonformat|spectatorifincompatible>
                 source <clocksync|fanout> · flag <seek|comments|chat|queue_lock|auto_advance|dsp_lock|max> <on|off|숫자>
                 retention <일> · anon [off] · vol [peer] <0-100>
    ── 라이브러리 catalog · search <말> · graph <아티스트ID> · scan [경로] · tidal [토큰] · qobuz [토큰]
    ── 개인      history · archives · playlists · makelist [아카이브ID] · load [플레이리스트ID] [replace] · m3u [ID] · share [아카이브ID]
    ── 운영      endpoints · pair · redeem <코드> · end <yes|no> · help · quit
    """);

string? Arg(string prefix) => args.FirstOrDefault(a => a.StartsWith(prefix))?[prefix.Length..];
