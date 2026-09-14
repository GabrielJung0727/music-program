using System.Net;
using Microsoft.Extensions.FileProviders;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Mono.Core;
using Mono.Protocol;
using Mono.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Mono:ControlUrl"] ?? "http://127.0.0.1:7702");

// 설치 루트 밖이다. 그 안에 두면 재설치 한 번에 카탈로그와 토큰이 사라진다.
var data = UserPaths.Resolve("data");
MigrateLegacyData(Path.Combine(AppContext.BaseDirectory, "data"), data);
var library = builder.Configuration["Mono:LibraryRoot"] ?? Path.Combine(data, "library");
// Mono:LibraryRoots 배열이 우선. 없으면 기존 단수 Mono:LibraryRoot 를 그대로 쓴다.
var configuredRoots = builder.Configuration.GetSection("Mono:LibraryRoots").Get<string[]>() ?? [];
var libraryRoots = configuredRoots.Length > 0 ? configuredRoots.ToList() : new List<string> { library };
var art = Path.Combine(data, "art");
Directory.CreateDirectory(data);
Directory.CreateDirectory(library);
Directory.CreateDirectory(art);

builder.Services.AddSingleton(new CatalogStore(Path.Combine(data, "catalog.db")));
builder.Services.AddSingleton(new HistoryStore(Path.Combine(data, "history.db")));
builder.Services.AddSingleton(new EndpointRegistry(Path.Combine(data, "endpoints.db")));
builder.Services.AddSingleton(new ZoneRegistry(Path.Combine(data, "zones.db")));
builder.Services.AddSingleton(new BackupService(data));
builder.Services.AddSingleton(new SetupStore(data));
var wikiHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
// 위키미디어 API 정책상 User-Agent 없는 요청은 403으로 거부된다.
wikiHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Mono-Control/1.0 (local hi-fi lounge app; https://github.com/mono-audio)");
builder.Services.AddSingleton(new WikipediaService(wikiHttp, Path.Combine(data, "wiki")));
builder.Services.AddSingleton(new ArtworkService(art));
builder.Services.AddSingleton<LibraryScanner>();
var controlUrl = builder.Configuration["Mono:ControlUrl"] ?? "http://127.0.0.1:7702";
builder.Services.AddSingleton(sp => new StreamingHub(
    sp.GetRequiredService<CatalogStore>(),
    Path.Combine(data, "streaming.json"),
    controlUrl));
builder.Services.AddSingleton<PairingService>();
builder.Services.AddSingleton(sp => new RoomManager(
    sp.GetRequiredService<CatalogStore>(),
    sp.GetRequiredService<HistoryStore>(),
    sp.GetRequiredService<StreamingHub>(),
    sp.GetRequiredService<EndpointRegistry>()));
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton(sp => new CommandProcessor(
    sp.GetRequiredService<RoomManager>(),
    sp.GetRequiredService<CatalogStore>(),
    sp.GetRequiredService<LibraryScanner>(),
    sp.GetRequiredService<StreamingHub>(),
    sp.GetRequiredService<PairingService>(),
    sp.GetRequiredService<HistoryStore>(),
    sp.GetRequiredService<EndpointRegistry>(),
    sp.GetRequiredService<ZoneRegistry>(),
    sp.GetRequiredService<WikipediaService>(),
    sp.GetRequiredService<BackupService>(),
    libraryRoots));
builder.Services.AddSingleton<RoomBroadcaster>();
builder.Services.AddSignalR().AddJsonProtocol(o =>
    o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

builder.Services.AddHostedService<CoreHostedService>();
builder.Services.AddHostedService<FanOutService>();
builder.Services.AddHostedService<TransportService>();
builder.Services.AddHostedService<RetentionService>();
builder.Services.AddHostedService(sp => ActivatorUtilities.CreateInstance<ScanScheduler>(sp, library));

builder.Services.AddSingleton<ControlSession>();

var app = builder.Build();

// data 가 설치 루트 밖으로 옮겨졌다면(0.4.2 이전에서 올라온 경우) 저장해 둔 절대 경로도
// 함께 옮겨야 한다. 파일은 따라왔지만 경로는 옛 자리를 가리키고 있어, 그대로 두면
// 라이브러리가 통째로 "파일 없음"이 된다. 되짚을 것이 없으면 아무 일도 하지 않는다.
{
    var moved = app.Services.GetRequiredService<CatalogStore>()
        .RebaseStoredPaths(UserPaths.LegacyDataRoot, data);
    var folders = app.Services.GetRequiredService<SetupStore>()
        .RebaseFolders(UserPaths.LegacyDataRoot, data);
    if (moved > 0 || folders)
        Console.WriteLine($"데이터 폴더 이동에 맞춰 경로를 고쳤습니다 — 트랙/아트 {moved}건" +
                          (folders ? ", 라이브러리 폴더 포함" : ""));
}

// Control UI(React, src/Mono.Web)는 wwwroot 에서 정적 서빙한다.
// WebView2 셸과 브라우저가 같은 번들을 본다.
var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (Directory.Exists(wwwroot))
{
    var files = new PhysicalFileProvider(wwwroot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = files,
        // 해시 파일명이 붙는 assets/ 는 오래 캐시해도 안전하고, index.html 은 절대 캐시하면 안 된다.
        OnPrepareResponse = ctx =>
        {
            var path = ctx.File.Name;
            ctx.Context.Response.Headers.CacheControl =
                path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    ? "no-cache, no-store, must-revalidate"
                    : "public, max-age=31536000, immutable";
        }
    });
}

app.UseWebSockets();

// 웹 UI의 컨트롤 플레인. TCP 7700 과 동일한 MonoMessage 규약.
app.Map("/ws/control", async (HttpContext http, ControlSession session, CancellationToken ct) =>
    await ControlWebSocket.HandleAsync(http, session, ct));

app.MapHub<LoungeHub>("/hub");

app.MapGet("/api/stream/tidal/{trackId}", async (HttpContext http, string trackId, StreamingHub hub, CancellationToken ct) =>
    await hub.ProxyTidalStreamAsync(http, trackId, ct));

app.MapGet("/oauth/callback", (string? code, string? state, string? error, StreamingHub streaming, CatalogStore catalog, RoomBroadcaster bus) =>
{
    if (!string.IsNullOrWhiteSpace(error))
        return Results.Content(OAuthPage("Tidal 연동을 취소했거나 거부했습니다.", error), "text/html; charset=utf-8");
    if (string.IsNullOrWhiteSpace(code))
        return Results.Content(OAuthPage("로그인 코드가 없습니다.", "code missing"), "text/html; charset=utf-8");
    try
    {
        var acc = streaming.CompleteOAuth(StreamingProvider.Tidal, code, state, null);
        var imported = streaming.LiveTrackCount();
        var note = streaming.LastImportNote;
        var accounts = new MonoMessage
        {
            Type = MessageTypes.LinkStreaming,
            Ok = acc.Connected,
            Provider = StreamingProvider.Tidal,
            Body = JsonSerializer.Serialize(streaming.AccountViews, LineFraming.JsonOptions)
        };
        var catalogMsg = new MonoMessage
        {
            Type = MessageTypes.Catalog,
            Body = JsonSerializer.Serialize(catalog.CatalogView(), LineFraming.JsonOptions)
        };
        _ = bus.PushCatalogAsync(accounts);
        _ = bus.PushCatalogAsync(catalogMsg);
        var title = imported > 0
            ? $"Tidal 연동 완료 · {imported}곡을 가져왔습니다."
            : "Tidal 로그인은 됐지만 곡 목록을 못 가져왔습니다.";
        return Results.Content(OAuthPage(title, note), "text/html; charset=utf-8");
    }
    catch (Exception ex)
    {
        return Results.Content(OAuthPage("Tidal 토큰 교환에 실패했습니다.", ex.Message), "text/html; charset=utf-8");
    }
});

app.MapGet("/api/health", (RoomManager rooms, CatalogStore catalog, EndpointRegistry endpoints) => Results.Ok(new
{
    ok = true,
    product = "Mono Core",
    rooms = rooms.List().Count,
    tracks = catalog.Tracks.Count,
    endpoints = endpoints.All().Count(e => e.Online),
    controlPort = CoreHostedService.ControlPort,
    matpPort = CoreHostedService.MatpPort
}));

app.MapGet("/api/rooms", (RoomManager rooms) => rooms.List().Select(r => new
{
    r.Id,
    r.Name,
    r.Mode,
    r.SourceMode,
    r.Playing,
    r.PathBadge,
    members = r.ControlPeerIds.Count,
    outputs = r.Outputs.Count
}));

app.MapGet("/api/rooms/{id}", (string id, RoomManager rooms) =>
{
    var room = rooms.Get(id);
    return room is null ? Results.NotFound() : Results.Ok(rooms.Snapshot(room));
});

app.MapGet("/api/catalog", (CatalogStore catalog) => catalog.CatalogView());
app.MapGet("/api/graph/{artistId}", (string artistId, CatalogStore catalog) => catalog.Graph(artistId));
app.MapGet("/api/endpoints", (EndpointRegistry endpoints) => endpoints.All());
app.MapGet("/api/archives", (HistoryStore history) => history.Archives);
app.MapGet("/api/playlists", (HistoryStore history) => history.Playlists);

// 앨범 아트는 Core가 캐시해 둔 파일만 내보낸다 (Control은 디코드하지 않는다).
app.MapGet("/api/art/{trackId}", (string trackId, CatalogStore catalog, ArtworkService artwork, HttpRequest req) =>
{
    var track = catalog.Tracks.GetValueOrDefault(trackId);
    var path = track?.ArtworkPath ?? (track is null ? null : catalog.Albums.GetValueOrDefault(track.AlbumId)?.ArtworkPath);
    if (path is null || !File.Exists(path))
        return Results.NotFound();

    if (int.TryParse(req.Query["w"], out var qw))
    {
        var w = Math.Clamp(qw, 32, 1024);
        var thumb = artwork.EnsureThumbnail(path, w);
        if (thumb is not null && File.Exists(thumb))
            return Results.File(thumb, ArtworkService.ContentType(thumb));
    }

    return Results.File(path, ArtworkService.ContentType(path));
});

// 첫 실행 마법사가 남긴 설정. 이 PC 에 매인 값이라 룸·카탈로그와 달리 Core 의 파일 한 장이다.
app.MapGet("/api/setup", (SetupStore setup) => Results.Ok(setup.Read()));

app.MapPut("/api/setup", async (HttpRequest req, SetupStore setup) =>
{
    var patch = await JsonSerializer.DeserializeAsync<JsonObject>(req.Body);
    if (patch is null) return Results.BadRequest(new { error = "JSON 객체가 필요합니다." });
    return Results.Ok(setup.Merge(patch));
});

app.MapDelete("/api/setup", (SetupStore setup) =>
{
    setup.Clear();
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/aliases/musicbrainz", async (CatalogStore catalog) =>
{
    var n = await catalog.EnrichAliasesFromMusicBrainzAsync();
    return Results.Ok(new { updated = n });
});

app.MapGet("/api/m3u/{playlistId}", (string playlistId, HistoryStore history, CatalogStore catalog) =>
{
    var playlist = history.Playlist(playlistId);
    if (playlist is null)
    {
        return Results.NotFound();
    }

    var body = history.ExportM3u(playlist, catalog);
    var name = new string(playlist.Title.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
    return Results.File(System.Text.Encoding.UTF8.GetBytes(body), "audio/x-mpegurl", $"{name}.m3u");
});

app.MapGet("/api/reactions/{trackId}", (string trackId, HistoryStore history) => history.TrackHeatmap(trackId));

app.MapGet("/api/wiki/{artistId}", async (string artistId, CatalogStore catalog, WikipediaService wiki) =>
{
    var artist = catalog.Artists.GetValueOrDefault(artistId);
    if (artist is null)
    {
        return Results.NotFound();
    }

    var candidates = new List<string> { artist.Name };
    candidates.AddRange(artist.AlternateNames);
    var summary = await wiki.GetSummaryAsync(artist.Id, candidates);
    return summary is null ? Results.NotFound() : Results.Ok(summary);
});

app.MapGet("/api/session/{archiveId}", (string archiveId, HistoryStore history, CatalogStore catalog) =>
{
    var archive = history.Archive(archiveId);
    return archive is null
        ? Results.NotFound()
        : Results.Text(history.ShareSummary(archive, catalog), "text/plain; charset=utf-8");
});

// SPA 딥링크. /api, /hub, /ws, /oauth 는 위에서 이미 잡혔으므로 여기 오지 않는다.
if (Directory.Exists(wwwroot))
{
    app.MapFallback(async ctx =>
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        await ctx.Response.SendFileAsync(Path.Combine(wwwroot, "index.html"));
    });
}

app.Run();

static void MigrateLegacyData(string legacy, string dest)
{
    try
    {
        if (!Directory.Exists(legacy) || string.Equals(
                Path.GetFullPath(legacy), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            return;
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(legacy, file);
            var target = Path.Combine(dest, rel);
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
    catch
    {
        // 마이그레이션 실패해도 새 경로로 계속한다.
    }
}

static string OAuthPage(string title, string? detail)
{
    var extra = string.IsNullOrWhiteSpace(detail)
        ? ""
        : $"<p style=\"color:#888;font-size:13px\">{WebUtility.HtmlEncode(detail)}</p>";
    return $"""
        <!doctype html><html lang="ko"><head><meta charset="utf-8"><title>Mono · Tidal</title></head>
        <body style="font-family:Segoe UI,sans-serif;background:#121317;color:#f2f2f6;display:flex;min-height:100vh;align-items:center;justify-content:center">
        <div style="max-width:28rem;text-align:center">
        <h1 style="font-weight:600;font-size:1.25rem">{WebUtility.HtmlEncode(title)}</h1>
        {extra}
        </div></body></html>
        """;
}
