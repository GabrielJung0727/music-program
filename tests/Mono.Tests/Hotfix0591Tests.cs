using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mono.Core;
using Mono.Output;
using Mono.Protocol;
using Mono.Shared;
using Xunit;

namespace Mono.Tests;

public class Hotfix0591Tests
{
    [Theory]
    [InlineData(1500, 0.25)]
    [InlineData(2500, 0.375)]
    public void FlacSeekStartsAtRequestedAudioInsteadOfBeginning(int position, double expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "seek-markers.flac");
        using var source = LocalFileRenderer.TryOpen(path);
        Assert.NotNull(source);
        var pcm = source.Read(position, 20, out var format);
        Assert.Equal(24, format.depth);
        Assert.True(pcm.Length >= 6);
        var sample = (pcm[0] | pcm[1] << 8 | (sbyte)pcm[2] << 16) / 8388608.0;
        Assert.InRange(sample, expected - 0.001, expected + 0.001);
        using var coreSource = new MediaFoundationSlicer(path);
        var corePcm = coreSource.Read(position, 20);
        var coreSample = (corePcm[0] | corePcm[1] << 8 | (sbyte)corePcm[2] << 16) / 8388608.0;
        Assert.InRange(coreSample, expected - 0.001, expected + 0.001);
        // Backward and repeated seeks on the same decoder must discard its
        // previous compressed-frame buffer too.
        foreach (var target in new[] { 3500, 500, 2500, 1500 })
        {
            var next = source.Read(target, 20, out _);
            var value = (next[0] | next[1] << 8 | (sbyte)next[2] << 16) / 8388608.0;
            var marker = (target / 1000 + 1) * 0.125;
            Assert.InRange(value, marker - 0.001, marker + 0.001);
            var decoded = coreSource.Read(target, 20);
            var coreValue = (decoded[0] | decoded[1] << 8 | (sbyte)decoded[2] << 16) / 8388608.0;
            Assert.InRange(coreValue, marker - 0.001, marker + 0.001);
        }
    }

    [Fact]
    public void SavedLibraryFoldersRemainVisibleAcrossRestartsAndChanges()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-hotfix-" + Guid.NewGuid().ToString("N"));
        var setup = new SetupStore(dir);
        setup.Merge(new JsonObject { ["folders"] = new JsonArray("D:\\Music", "d:\\music") });
        var reloaded = new SetupStore(dir);
        Assert.Equal(new[] { "default", "D:\\Music" }, reloaded.LibraryFolders(["default"]));
        setup.Merge(new JsonObject { ["folders"] = new JsonArray("E:\\Music") });
        Assert.Equal(new[] { "default", "E:\\Music" }, reloaded.LibraryFolders(["default"]));
    }

    [Fact]
    public void AsioFailureIsResetBeforeTheNextTrack()
    {
        var hostType = typeof(AsioAudioRenderer).Assembly.GetType("Mono.Output.AsioStaHost")!;
        var host = Activator.CreateInstance(hostType)!;
        using var renderer = (AsioAudioRenderer)Activator.CreateInstance(typeof(AsioAudioRenderer),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [host, "test-driver"], null)!;
        typeof(AsioAudioRenderer).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(renderer, AudioDeviceState.DeviceBusyLocked);
        renderer.Flush();
        Assert.Equal(AudioDeviceState.Idle, renderer.GetCurrentState());
    }

    [Fact]
    public async Task RescanPublishesChangedTagsWithoutAddingTracks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-hotfix-" + Guid.NewGuid().ToString("N"));
        var lib = Path.Combine(dir, "music");
        Directory.CreateDirectory(lib);
        var path = Path.Combine(lib, "song.wav");
        TestAudio.WriteSilentWav(path);
        using var catalog = new CatalogStore(Path.Combine(dir, "c.db"));
        var scanner = new LibraryScanner(catalog, new ArtworkService(Path.Combine(dir, "art")));
        scanner.Scan(lib);
        using (var tagged = TagLib.File.Create(path))
        {
            tagged.Tag.Performers = ["Track Performer"];
            tagged.Tag.AlbumArtists = ["Various Artists"];
            tagged.Save();
        }
        var connections = new ConnectionRegistry();
        var delivered = new TaskCompletionSource<MonoMessage>();
        connections.Controls["test"] = msg => { delivered.TrySetResult(msg); return Task.CompletedTask; };
        var rooms = new RoomManager(catalog, new HistoryStore(Path.Combine(dir, "h.db")));
        var broadcaster = new RoomBroadcaster(connections, rooms, catalog, new StreamingHub(catalog, Path.Combine(dir, "stream.json")));
        var room = rooms.Create("test", "Test", RoomMode.Solo, "Test");
        rooms.Enqueue(room.Id, "test", catalog.Tracks.Values.Single(t => t.LocalPath == path).Id);
        var roomDelivered = new TaskCompletionSource<MonoMessage>();
        connections.Controls["test"] = msg =>
        {
            if (msg.Type == MessageTypes.Catalog) delivered.TrySetResult(msg);
            if (msg.Type == MessageTypes.RoomState) roomDelivered.TrySetResult(msg);
            return Task.CompletedTask;
        };
        using var scheduler = new ScanScheduler(scanner, catalog, broadcaster,
            new ConfigurationBuilder().Build(), NullLogger<ScanScheduler>.Instance, [lib], rooms: rooms);
        await scheduler.StartAsync(default);
        try
        {
            var msg = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Contains("Track Performer", msg.Body);
            Assert.Contains("Track Performer", (await roomDelivered.Task.WaitAsync(TimeSpan.FromSeconds(3))).Body);
        }
        finally { await scheduler.StopAsync(default); }
    }

    [Fact]
    public void OldBroadcastCannotUndoASeek()
    {
        var timeline = new Timeline();
        timeline.Apply(new MonoMessage { Type = MessageTypes.RoomState, Epoch = 9,
            TrackId = "song", Playing = false, MediaTimeAtOriginMs = 90000 });
        timeline.Apply(new MonoMessage { Type = MessageTypes.RoomState, Epoch = 8,
            TrackId = "song", Playing = false, MediaTimeAtOriginMs = 0 });
        Assert.Equal(90000, timeline.MediaTimeMs(0));
        Assert.Equal(9, timeline.Epoch);
    }

    [Fact]
    public void AutoplayDoesNotRecommendSyntheticOrMissingLocalTracks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-hotfix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var catalog = new CatalogStore(Path.Combine(dir, "catalog.db"));
        var artist = new Artist { Id = "artist", Name = "Artist" };
        var album = new Album { Id = "album", Title = "Album", ArtistId = artist.Id };
        var path = Path.Combine(dir, "real.wav");
        TestAudio.WriteSilentWav(path);
        catalog.UpsertTrack(new Track { Id = "real", Title = "Real", AlbumId = album.Id,
            ArtistId = artist.Id, LocalPath = path }, album, artist);
        catalog.UpsertTrack(new Track { Id = "missing", Title = "Missing", AlbumId = album.Id,
            ArtistId = artist.Id, LocalPath = path + ".missing" }, album, artist);
        Assert.Equal(new[] { "real" }, catalog.Recommend(null, [], 100).Select(t => t.Id));
    }

    [Fact]
    public void NewPlaybackEpochClearsFailedDeviceState()
    {
        using var renderer = new WasapiOutputDevice(null, DeviceMode.SystemShared);
        typeof(WasapiOutputDevice).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(renderer, AudioDeviceState.DeviceBusyLocked);
        renderer.Flush();
        Assert.Equal(AudioDeviceState.Idle, renderer.GetCurrentState());
    }

    [Fact]
    public void NativeDsdIsNotOpenedAsOrdinaryLocalPcm()
    {
        // The clock-sync reader has no native-DSD negotiation. A DSF must never
        // be silently turned into DoP bytes labelled as ordinary PCM.
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dsf");
        using (var file = File.Create(path))
        using (var w = new BinaryWriter(file))
        {
            w.Write("DSD "u8); w.Write(28L); w.Write(8300L); w.Write(0L);
            w.Write("fmt "u8); w.Write(52L); w.Write(1); w.Write(0); w.Write(2);
            w.Write(2); w.Write(11289600); w.Write(1); w.Write(32768L); w.Write(4096); w.Write(0);
            w.Write("data"u8); w.Write(8204L); w.Write(new byte[8192]);
        }
        try { using var source = LocalFileRenderer.TryOpen(path); Assert.Null(source); }
        finally { File.Delete(path); }
    }
}
