using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// data 폴더를 설치 루트 밖으로 옮기고 나면, 저장해 둔 절대 경로가 옛 자리를 가리킨 채
/// 남는다. 파일은 따라왔는데 경로만 어긋나서 라이브러리가 통째로 비어 보이는 일을 막는다.
/// </summary>
public class PathRebaseTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-rebase-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── catalog.db ────────────────────────────────────────────────────────────

    private static void SetTrackPath(string db, string trackId, string localPath, string art)
    {
        using var con = new SqliteConnection($"Data Source={db}");
        con.Open();
        con.Execute("UPDATE tracks SET local_path=$p, art=$a WHERE id=$id",
            ("$p", localPath), ("$a", art), ("$id", trackId));
    }

    private static (string? path, string? art) ReadTrackPath(string db, string trackId)
    {
        using var con = new SqliteConnection($"Data Source={db}");
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT local_path, art FROM tracks WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", trackId);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? (r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1))
            : (null, null);
    }

    [Fact]
    public void MovesStoredTrackAndArtworkPathsToTheNewRoot()
    {
        var db = Path.Combine(NewDir(), "c.db");
        var store = new CatalogStore(db);
        var id = store.Tracks.Keys.First();

        var oldRoot = @"C:\Users\x\AppData\Local\Mono\data";
        var newRoot = @"C:\Users\x\AppData\Local\MonoData\data";
        SetTrackPath(db, id, Path.Combine(oldRoot, "library", "a.flac"), Path.Combine(oldRoot, "art", "a.jpg"));

        var changed = store.RebaseStoredPaths(oldRoot, newRoot);

        Assert.True(changed >= 2);
        var (path, art) = ReadTrackPath(db, id);
        Assert.Equal(Path.Combine(newRoot, "library", "a.flac"), path);
        Assert.Equal(Path.Combine(newRoot, "art", "a.jpg"), art);
    }

    [Fact]
    public void LeavesPathsThatLiveSomewhereElseAlone()
    {
        var db = Path.Combine(NewDir(), "c.db");
        var store = new CatalogStore(db);
        var id = store.Tracks.Keys.First();

        var mine = @"D:\Music\album\b.flac";
        SetTrackPath(db, id, mine, mine);

        store.RebaseStoredPaths(@"C:\Users\x\AppData\Local\Mono\data", @"C:\Users\x\AppData\Local\MonoData\data");

        var (path, _) = ReadTrackPath(db, id);
        Assert.Equal(mine, path);
    }

    [Fact]
    public void MonoDoesNotSwallowMonoData()
    {
        // 경계를 안 붙이면 ...\Mono 가 ...\MonoData 의 접두사라 이미 옮긴 경로를 또 옮긴다.
        var db = Path.Combine(NewDir(), "c.db");
        var store = new CatalogStore(db);
        var id = store.Tracks.Keys.First();

        var already = @"C:\Users\x\AppData\Local\MonoData\data\library\a.flac";
        SetTrackPath(db, id, already, already);

        store.RebaseStoredPaths(@"C:\Users\x\AppData\Local\Mono", @"C:\Users\x\AppData\Local\MonoData");

        var (path, _) = ReadTrackPath(db, id);
        Assert.Equal(already, path);
    }

    [Fact]
    public void UnderscoresInPathsAreNotTreatedAsWildcards()
    {
        // LIKE 로 짰다면 _ 가 아무 글자나 되어 엉뚱한 행까지 바꿨을 자리다.
        var db = Path.Combine(NewDir(), "c.db");
        var store = new CatalogStore(db);
        var id = store.Tracks.Keys.First();

        var other = @"C:\Users\x\AppData\Local\MonoXdata\library\a.flac";
        SetTrackPath(db, id, other, other);

        store.RebaseStoredPaths(@"C:\Users\x\AppData\Local\Mono_data", @"C:\Users\x\AppData\Local\MonoData\data");

        var (path, _) = ReadTrackPath(db, id);
        Assert.Equal(other, path);
    }

    [Fact]
    public void NothingToDoIsNotAChange()
    {
        var db = Path.Combine(NewDir(), "c.db");
        var store = new CatalogStore(db);
        Assert.Equal(0, store.RebaseStoredPaths(@"C:\same", @"C:\same"));
        Assert.Equal(0, store.RebaseStoredPaths("", @"C:\x"));
    }

    // ── setup.json ────────────────────────────────────────────────────────────

    [Fact]
    public void WizardFoldersFollowTheDataDirectory()
    {
        var dir = NewDir();
        var store = new SetupStore(dir);
        var oldRoot = @"C:\Users\x\AppData\Local\Mono\data";
        var newRoot = @"C:\Users\x\AppData\Local\MonoData\data";
        store.Merge(new JsonObject
        {
            ["completed"] = true,
            ["folders"] = new JsonArray(oldRoot + @"\library", @"D:\Music"),
        });

        Assert.True(store.RebaseFolders(oldRoot, newRoot));

        var folders = store.Read()["folders"]!.AsArray().Select(f => f!.GetValue<string>()).ToList();
        Assert.Equal(newRoot + @"\library", folders[0]);
        Assert.Equal(@"D:\Music", folders[1]);   // 내 폴더는 건드리지 않는다
    }

    [Fact]
    public void WizardFoldersAreLeftAloneWhenNothingMatches()
    {
        var dir = NewDir();
        var store = new SetupStore(dir);
        store.Merge(new JsonObject { ["folders"] = new JsonArray(@"D:\Music") });

        Assert.False(store.RebaseFolders(@"C:\Users\x\AppData\Local\Mono\data",
                                         @"C:\Users\x\AppData\Local\MonoData\data"));
    }
}
