using Avalonia;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mono.Control.Services;
using Mono.Shared;
using Velopack;

namespace Mono.Control;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        if (args.Any(a => a is "--check-update" or "--apply-update"))
        {
            RunUpdateCli(args).GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static async Task RunUpdateCli(string[] args)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mono", "update-check.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        void Log(string line)
        {
            File.AppendAllText(logPath, DateTimeOffset.Now.ToString("o") + " " + line + Environment.NewLine);
        }

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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
