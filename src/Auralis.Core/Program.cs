using System.Text.Json;
using Auralis.Core;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Auralis:ControlUrl"] ?? "http://127.0.0.1:7702");

var data = Path.Combine(AppContext.BaseDirectory, "data");
var library = builder.Configuration["Auralis:LibraryRoot"] ?? Path.Combine(data, "library");
var art = Path.Combine(data, "art");
Directory.CreateDirectory(data);
Directory.CreateDirectory(library);
Directory.CreateDirectory(art);

builder.Services.AddSingleton(new CatalogStore(Path.Combine(data, "catalog.db")));
builder.Services.AddSingleton(new HistoryStore(Path.Combine(data, "history.db")));
builder.Services.AddSingleton(new EndpointRegistry(Path.Combine(data, "endpoints.db")));
builder.Services.AddSingleton(new ArtworkService(art));
builder.Services.AddSingleton<LibraryScanner>();
builder.Services.AddSingleton<StreamingHub>();
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
    library));
builder.Services.AddSingleton<RoomBroadcaster>();
builder.Services.AddSignalR().AddJsonProtocol(o =>
    o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

builder.Services.AddHostedService<CoreHostedService>();
builder.Services.AddHostedService<FanOutService>();
builder.Services.AddHostedService<TransportService>();
builder.Services.AddHostedService<RetentionService>();
builder.Services.AddHostedService(sp => ActivatorUtilities.CreateInstance<ScanScheduler>(sp, library));

var app = builder.Build();
app.UseDefaultFiles();

// Control UI는 Core가 갱신되면 바로 반영돼야 한다 — ETag로 재검증하게 두고 stale 캐시를 막는다.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate"
});
app.MapHub<LoungeHub>("/hub");

app.MapGet("/api/health", (RoomManager rooms, CatalogStore catalog, EndpointRegistry endpoints) => Results.Ok(new
{
    ok = true,
    product = "Auralis Core",
    rooms = rooms.List().Count,
    tracks = catalog.Tracks.Count,
    endpoints = endpoints.All().Count(e => e.Online),
    controlPort = CoreHostedService.ControlPort,
    aatpPort = CoreHostedService.AatpPort
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
app.MapGet("/api/art/{trackId}", (string trackId, CatalogStore catalog) =>
{
    var track = catalog.Tracks.GetValueOrDefault(trackId);
    var path = track?.ArtworkPath ?? (track is null ? null : catalog.Albums.GetValueOrDefault(track.AlbumId)?.ArtworkPath);
    if (path is null || !File.Exists(path))
    {
        return Results.NotFound();
    }

    return Results.File(path, ArtworkService.ContentType(path));
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

app.MapGet("/api/session/{archiveId}", (string archiveId, HistoryStore history, CatalogStore catalog) =>
{
    var archive = history.Archive(archiveId);
    return archive is null
        ? Results.NotFound()
        : Results.Text(history.ShareSummary(archive, catalog), "text/plain; charset=utf-8");
});

app.Run();
