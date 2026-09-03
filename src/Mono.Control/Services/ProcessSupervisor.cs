using System.Diagnostics;

namespace Mono.Control.Services;

/// <summary>Core·Output exe를 콘솔 없이 기동·종료한다.</summary>
public sealed class ProcessSupervisor
{
    private Process? _core;
    private Process? _output;

    public bool CoreRunning => _core is { HasExited: false };
    public bool OutputRunning => _output is { HasExited: false };
    public string? LastError { get; private set; }

    public string BaseDir => AppContext.BaseDirectory;

    public async Task<bool> EnsureCoreAsync(CancellationToken ct = default)
    {
        if (CoreRunning) return true;
        if (await WaitForPortAsync(7700, TimeSpan.FromMilliseconds(400), ct)) return true;

        var exe = FindExe("Mono.Core.exe", "Mono.Core");
        if (exe is null)
        {
            LastError = "Mono.Core.exe를 찾을 수 없습니다. Control과 같은 폴더에 두세요.";
            return false;
        }

        try
        {
            _core = StartSilent(exe);
            for (var i = 0; i < 40; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (await WaitForPortAsync(7700, TimeSpan.FromMilliseconds(250), ct)) return true;
            }

            LastError = "Core가 시작됐지만 포트 7700에 응답하지 않습니다.";
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public bool StartOutput(string? roomId, string host = "127.0.0.1")
    {
        if (OutputRunning) return true;
        var exe = FindExe("Mono.Output.exe", "Mono.Output");
        if (exe is null)
        {
            LastError = "Mono.Output.exe를 찾을 수 없습니다.";
            return false;
        }

        var args = $"--host={host}";
        if (!string.IsNullOrWhiteSpace(roomId)) args += $" --room={roomId}";

        try
        {
            _output = StartSilent(exe, args);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public void StopOutput()
    {
        TryKill(ref _output);
    }

    public void StopAll()
    {
        TryKill(ref _output);
        TryKill(ref _core);
    }

    private static Process StartSilent(string exePath, string args = "")
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var p = Process.Start(psi) ?? throw new InvalidOperationException("프로세스 기동 실패: " + exePath);
        return p;
    }

    private string? FindExe(string fileName, string folderName)
    {
        var candidates = new[]
        {
            Path.Combine(BaseDir, fileName),
            Path.Combine(BaseDir, folderName, fileName),
            Path.GetFullPath(Path.Combine(BaseDir, "..", folderName, fileName)),
            Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "..", "..", "dist", folderName, fileName)),
            Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "..", "Mono.Core", "bin", "Debug", "net8.0", fileName)),
            Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "..", "Mono.Output", "bin", "Debug", "net8.0", fileName)),
        };

        // Core debug path special-case when looking for Core
        if (fileName.Contains("Core", StringComparison.OrdinalIgnoreCase))
        {
            candidates =
            [
                Path.Combine(BaseDir, fileName),
                Path.Combine(BaseDir, folderName, fileName),
                Path.GetFullPath(Path.Combine(BaseDir, "..", folderName, fileName)),
                Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "..", "Mono.Core", "bin", "Debug", "net8.0", "Mono.Core.exe")),
                Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "..", "..", "dist", "Mono.Core", "Mono.Core.exe")),
            ];
        }
        else if (fileName.Contains("Output", StringComparison.OrdinalIgnoreCase))
        {
            candidates =
            [
                Path.Combine(BaseDir, fileName),
                Path.Combine(BaseDir, folderName, fileName),
                Path.GetFullPath(Path.Combine(BaseDir, "..", folderName, fileName)),
                Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "..", "Mono.Output", "bin", "Debug", "net8.0", "Mono.Output.exe")),
                Path.GetFullPath(Path.Combine(BaseDir, "..", "..", "..", "..", "..", "dist", "Mono.Output", "Mono.Output.exe")),
            ];
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connect = client.ConnectAsync("127.0.0.1", port);
            var completed = await Task.WhenAny(connect, Task.Delay(timeout, ct));
            return completed == connect && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static void TryKill(ref Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch { /* ignore */ }
        finally
        {
            process?.Dispose();
            process = null;
        }
    }
}
