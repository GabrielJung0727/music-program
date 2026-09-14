using Mono.Shared;
using Xunit;

namespace Mono.Tests;

/// <summary>
/// 사용자 데이터를 설치 루트 밖으로 옮기는 이사. 0.4.1 에서 재설치 한 번에 카탈로그와
/// 로그인이 사라진 적이 있어, 옮기다 만 상태에서 무엇을 덮어쓰는지가 중요하다.
/// </summary>
public class UserPathsTests
{
    private static (string from, string to) NewPair()
    {
        var root = Path.Combine(Path.GetTempPath(), "mono-paths-" + Guid.NewGuid().ToString("n"));
        var from = Path.Combine(root, "Mono");
        var to = Path.Combine(root, "MonoData");
        Directory.CreateDirectory(from);
        Directory.CreateDirectory(to);
        return (from, to);
    }

    [Fact]
    public void MovesFilesAndFoldersOutOfTheInstallRoot()
    {
        var (from, to) = NewPair();
        Directory.CreateDirectory(Path.Combine(from, "data"));
        File.WriteAllText(Path.Combine(from, "data", "catalog.db"), "catalog");
        File.WriteAllText(Path.Combine(from, "prefs.ini"), "github_token=abc");

        UserPaths.Migrate(from, to, ["data", "prefs.ini"]);

        Assert.Equal("catalog", File.ReadAllText(Path.Combine(to, "data", "catalog.db")));
        Assert.Equal("github_token=abc", File.ReadAllText(Path.Combine(to, "prefs.ini")));
        Assert.False(Directory.Exists(Path.Combine(from, "data")));
        Assert.False(File.Exists(Path.Combine(from, "prefs.ini")));
    }

    [Fact]
    public void LeavesWhatIsAlreadyAtTheDestination()
    {
        var (from, to) = NewPair();
        File.WriteAllText(Path.Combine(from, "prefs.ini"), "old");
        File.WriteAllText(Path.Combine(to, "prefs.ini"), "current");

        UserPaths.Migrate(from, to, ["prefs.ini"]);

        // 새 자리 것이 진실이다. 옛것으로 덮으면 이미 쓴 설정이 되돌아간다.
        Assert.Equal("current", File.ReadAllText(Path.Combine(to, "prefs.ini")));
    }

    [Fact]
    public void MissingEntriesAreNotAnError()
    {
        var (from, to) = NewPair();
        UserPaths.Migrate(from, to, ["data", "webview", "prefs.ini"]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(to));
    }

    [Fact]
    public void RunningTwiceIsHarmless()
    {
        var (from, to) = NewPair();
        File.WriteAllText(Path.Combine(from, "credentials.env"), "secret");

        UserPaths.Migrate(from, to, ["credentials.env"]);
        UserPaths.Migrate(from, to, ["credentials.env"]);

        Assert.Equal("secret", File.ReadAllText(Path.Combine(to, "credentials.env")));
    }

    [Fact]
    public void DataDirectoryIsNotInsideTheInstallRoot()
    {
        // 이 한 줄이 이 변경의 전부다. 같은 트리로 돌아가면 재설치가 다시 지운다.
        var install = Path.GetFullPath(UserPaths.InstallRoot);
        var data = Path.GetFullPath(UserPaths.Root);

        Assert.NotEqual(install, data);
        Assert.False(data.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryUserOwnedEntryIsCarriedOver()
    {
        // 목록에서 빠진 항목은 조용히 설치 루트에 남아 다음 재설치 때 사라진다.
        Assert.Contains("data", UserPaths.UserOwnedEntries);
        Assert.Contains("webview", UserPaths.UserOwnedEntries);
        Assert.Contains("prefs.ini", UserPaths.UserOwnedEntries);
        Assert.Contains("credentials.env", UserPaths.UserOwnedEntries);
    }
}
