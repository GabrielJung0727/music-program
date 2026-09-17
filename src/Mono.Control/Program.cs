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

        // Mono 는 한 번에 하나만 뜬다.
        //
        // 두 벌이 뜨면 각자 Core 와 Output 워커를 띄우려 한다. 포트는 한 벌만 잡으므로
        // 나중 것은 앞선 인스턴스의 Core 에 붙고, 같은 PC 가 두 사람처럼 보인다 —
        // 같은 이름의 라운지가 둘씩 생기고, 두 워커가 같은 DAC 를 배타로 열려고 다툰다.
        // 무엇이 잘못됐는지 화면에는 아무것도 안 나온다.
        using var single = new Mutex(true, SingleInstanceName, out var isFirst);
        if (!isFirst)
        {
            // 이미 떠 있는 창을 앞으로 부르고 조용히 물러난다. 사용자는 아이콘을 한 번 더
            // 눌렀을 뿐이므로 오류창을 띄울 일이 아니다.
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, ShowExistingMessage, IntPtr.Zero, IntPtr.Zero);
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

    /// <summary>
    /// 이 PC 의 이 사용자에게 하나. Local\ 접두사라 다른 사용자 세션과는 겹치지 않는다 —
    /// 한 PC 에 두 사람이 로그인해 각자 Mono 를 쓰는 건 막을 이유가 없다.
    /// </summary>
    private const string SingleInstanceName = @"Local\Mono.Control.SingleInstance";

    /// <summary>이미 떠 있는 창을 앞으로 부르는 신호. 이름으로 등록해 값이 겹치지 않게 한다.</summary>
    public static readonly uint ShowExistingMessage =
        NativeMethods.RegisterWindowMessage("Mono.Control.ShowExisting");

    internal static class NativeMethods
    {
        public static readonly IntPtr HWND_BROADCAST = new(0xffff);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string lpString);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
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

    private static string CrashLogPath() => UserPaths.Resolve("crash.log");

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
        var logPath = UserPaths.Resolve("update-check.log");
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
