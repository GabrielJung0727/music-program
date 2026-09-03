using System.Diagnostics;
using System.Reflection;
using Mono.Control.ViewModels;
using Mono.Shared;
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

    public bool IsInstalled
    {
        get
        {
            try { return CreateManager().IsInstalled; }
            catch { return false; }
        }
    }

    public UpdateManager CreateManager()
    {
        var feed = Environment.GetEnvironmentVariable("MONO_UPDATE_FEED");
        if (string.IsNullOrWhiteSpace(feed))
            feed = Prefs.Get("update_feed", "");
        if (!string.IsNullOrWhiteSpace(feed))
            return new UpdateManager(feed.Trim());

        return new UpdateManager(new GithubSource(GitHubRepo, accessToken: ResolveGitHubToken(), prerelease: true));
    }

    public async Task<string> CheckAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var mgr = CreateManager();
        if (!mgr.IsInstalled)
        {
            Pending = null;
            return "설치 프로그램으로 설치한 뒤에만 업데이트를 받을 수 있습니다.";
        }

        var info = await mgr.CheckForUpdatesAsync();
        ct.ThrowIfCancellationRequested();
        Pending = info;
        if (info is null)
            return "최신 버전입니다.";

        return $"{info.TargetFullRelease.Version}을(를) 설치할 수 있습니다.";
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

    public static string? ResolveGitHubToken()
    {
        foreach (var key in new[] { "MONO_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN" })
        {
            var env = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();
        }

        var saved = Prefs.Get("github_token", "");
        if (!string.IsNullOrWhiteSpace(saved))
            return saved.Trim();

        var fromGh = TryReadGhAuthToken();
        if (!string.IsNullOrWhiteSpace(fromGh))
        {
            try { Prefs.Set("github_token", fromGh); }
            catch { /* prefs write is optional */ }
            return fromGh;
        }

        return null;
    }

    internal static string? TryReadGhAuthToken()
    {
        var gh = FindGhExecutable();
        if (gh is null) return null;
        try
        {
            var psi = new ProcessStartInfo(gh, "auth token")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }
            var token = p.StandardOutput.ReadToEnd().Trim();
            if (p.ExitCode != 0 || token.Length < 8) return null;
            if (token.Contains('\n') || token.Contains(' ')) return null;
            return token;
        }
        catch
        {
            return null;
        }
    }

    internal static string? FindGhExecutable()
    {
        var names = new[] { "gh.exe", "gh" };
        foreach (var name in names)
        {
            try
            {
                var psi = new ProcessStartInfo(name, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                if (!p.WaitForExit(2000))
                {
                    try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    continue;
                }
                if (p.ExitCode == 0) return name;
            }
            catch { /* try next path */ }
        }

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var candidate in new[]
                 {
                     Path.Combine(pf, "GitHub CLI", "gh.exe"),
                     Path.Combine(pf86, "GitHub CLI", "gh.exe"),
                     Path.Combine(local, "GitHub CLI", "gh.exe"),
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
