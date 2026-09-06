using System.IO.Compression;
using Mono.Core;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 백업은 DB만 묶는다. 음원 파일을 넣으면 수십 GB가 되고, 문서가 금지한다.
/// </summary>
public class BackupTests
{
    private static string NewDataDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mono-" + Guid.NewGuid().ToString("n"), "data");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "catalog.db"), "catalog");
        File.WriteAllText(Path.Combine(dir, "history.db"), "history");
        File.WriteAllText(Path.Combine(dir, "endpoints.db"), "endpoints");
        File.WriteAllText(Path.Combine(dir, "zones.db"), "zones");
        return dir;
    }

    [Fact]
    public void BackupContainsEveryDatabase()
    {
        var dir = NewDataDir();

        var zipPath = new BackupService(dir).Create();

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.Name).ToHashSet();
        Assert.Contains("catalog.db", names);
        Assert.Contains("history.db", names);
        Assert.Contains("endpoints.db", names);
        Assert.Contains("zones.db", names);
    }

    [Fact]
    public void BackupLeavesOutAudioFiles()
    {
        var dir = NewDataDir();
        var library = Path.Combine(dir, "library");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "song.flac"), "not really audio");

        var zipPath = new BackupService(dir).Create();

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.DoesNotContain(zip.Entries, e => e.Name.EndsWith(".flac"));
    }

    [Fact]
    public void MissingDatabaseIsSkippedNotFatal()
    {
        // 아직 한 번도 안 쓴 DB 가 있어도 백업은 되어야 한다.
        var dir = NewDataDir();
        File.Delete(Path.Combine(dir, "zones.db"));

        var zipPath = new BackupService(dir).Create();

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.Name == "catalog.db");
        Assert.DoesNotContain(zip.Entries, e => e.Name == "zones.db");
    }

    [Fact]
    public void EachBackupGetsItsOwnFile()
    {
        var dir = NewDataDir();
        var service = new BackupService(dir);

        var first = service.Create();
        Thread.Sleep(1100);
        var second = service.Create();

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }
}
