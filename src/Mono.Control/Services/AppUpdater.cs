using System.Reflection;
using Mono.Control.ViewModels;
using Velopack;
using Velopack.Sources;

namespace Mono.Control.Services;

/// <summary>
/// GitHub Releases(또는 로컬 피드)에서 패키지를 받아 창을 닫고 교체한 뒤 다시 실행한다.
/// Velopack 설치본에서만 동작한다. zip 실행은 IsInstalled=false.
/// </summary>
public sealed class AppUpdater
{
    public const string GitHubRepo = "https://github.com/GabrielJung0727/music-program";
    public const string PackId = "Mono";

    public string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public UpdateInfo? Pending { get; private set; }

    public UpdateManager CreateManager()
    {
        var feed = Environment.GetEnvironmentVariable("MONO_UPDATE_FEED");
        if (string.IsNullOrWhiteSpace(feed))
            feed = Prefs.Get("update_feed", "");
        if (!string.IsNullOrWhiteSpace(feed))
            return new UpdateManager(feed.Trim());

        return new UpdateManager(new GithubSource(GitHubRepo, accessToken: null, prerelease: true));
    }

    public async Task<string> CheckAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var mgr = CreateManager();
        if (!mgr.IsInstalled)
        {
            Pending = null;
            return "zip 실행본은 자동 업데이트가 없습니다. 설치 프로그램(Setup)으로 설치한 뒤에만 됩니다.";
        }

        var info = await mgr.CheckForUpdatesAsync();
        ct.ThrowIfCancellationRequested();
        Pending = info;
        if (info is null)
            return $"최신입니다. (현재 {CurrentVersion})";

        return $"새 버전 {info.TargetFullRelease.Version}을(를) 받을 수 있습니다.";
    }

    public async Task DownloadAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (Pending is null)
            throw new InvalidOperationException("받을 업데이트가 없습니다.");

        var mgr = CreateManager();
        await mgr.DownloadUpdatesAsync(Pending, p => progress?.Report(p));
        ct.ThrowIfCancellationRequested();
    }

    public void ApplyAndRestart()
    {
        if (Pending is null)
            throw new InvalidOperationException("적용할 업데이트가 없습니다.");

        CreateManager().ApplyUpdatesAndRestart(Pending);
    }
}
