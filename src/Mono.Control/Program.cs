using System.Diagnostics;
using Mono.Shared;
using Velopack;

namespace Mono.Control;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        InstallCrashLog();
        if (args.Any(a => a is "--check-update" or "--apply-update"))
        {
            RunUpdateCli(args).GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) =>
        {
            LogDiagnostic("ui", e.Exception);
            // 기록했으니 버틴다 — 한 화면의 오류로 앱 전체가 사라지면 안 된다.
        };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.Run(new ShellForm());
    }

    /// <summary>진단 로그 한 줄.</summary>
    public static void LogDiagnostic(string source, object? detail)
    {
        try
        {
            var path = CrashLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path,
                $"{DateTimeOffset.Now:o} [{source}] {detail}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* 로그조차 못 쓰면 할 수 있는 게 없다 */ }
    }

    private static string CrashLogPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mono", "crash.log");

    /// <summary>
    /// 처리되지 않은 예외를 파일로 남긴다. 없으면 창이 아무 말 없이 사라져
    /// 사용자도 우리도 원인을 모른다.
    /// </summary>
    private static void InstallCrashLog()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogDiagnostic("domain", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogDiagnostic("task", e.Exception);
            e.SetObserved();
        };
    }

    private static async Task RunUpdateCli(string[] args)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mono", "update-check.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        void Log(string line)
            => File.AppendAllText(logPath, DateTimeOffset.Now.ToString("o") + " " + line + Environment.NewLine);

        try
        {
            var updater = new AppUpdater();
            Log("current=" + updater.CurrentVersion);
            var msg = await updater.CheckAsync();
            Log(msg);
            if (!args.Contains("--apply-update") || updater.Pending is null)
                Environment.Exit(updater.Pending is null ? 0 : 2);

            Log("downloading " + updater.Pending.TargetFullRelease.Version);
            await updater.DownloadAsync(new Progress<int>(p => Log("progress=" + p)));
            Log("apply-and-restart");
            updater.ApplyAndRestart();
        }
        catch (Exception ex)
        {
            Log("error " + UpdateCheckErrors.Describe(ex));
            Environment.Exit(1);
        }
    }
}
