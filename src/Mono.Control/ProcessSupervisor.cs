using System.Diagnostics;
using System.Threading;

namespace Mono.Control;

/// <summary>Core·Output exe를 콘솔 없이 기동·종료한다.</summary>
public sealed class ProcessSupervisor
{
    /// <summary>드라이버가 얼어붙었을 때 워커를 끊고 다시 세우는 데 허용하는 시간.</summary>
    private const int CleanKillMs = 500;

    /// <summary>재기동 폭주 방지: 이 창 안에서 이 횟수를 넘기면 멈추고 사용자에게 알린다.</summary>
    private const int RestartBudget = 3;
    private static readonly TimeSpan RestartWindow = TimeSpan.FromSeconds(30);

    private Process? _core;
    private Process? _output;
    private string? _outputHost;

    /// <summary>지금 돌고 있는 워커를 띄울 때 쓴 인자. 규격이 바뀌었는지 이걸로 판정한다.</summary>
    private string? _outputArgs;
    private bool _outputIntentionallyStopped;
    private readonly Queue<DateTimeOffset> _restarts = new();

    public bool CoreRunning => _core is { HasExited: false };
    public bool OutputRunning => _output is { HasExited: false };

    /// <summary>Output 을 어느 룸으로 띄웠는지. 룸이 바뀌면 다시 띄워야 하므로 기억한다.</summary>
    public string? OutputRoomId { get; private set; }
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

    /// <summary>워커가 죽거나 얼면 이 이벤트로 알린다. UI 가 사유를 띄운다.</summary>
    public event Action<string>? OutputFaulted;

    /// <summary>
    /// 출력 드라이버 백엔드: exclusive · exclusive-strict · shared · asio.
    /// RME 같은 전문 인터페이스는 하드웨어 클럭이 외부에서 고정돼 WASAPI 배타 요청이
    /// 거절되는 일이 흔하다. 그런 장치는 ASIO 로 가야 비트퍼펙트가 성립한다.
    ///
    /// 기본값 exclusive 는 배타가 거절되면 공유로 내려가 소리는 계속 낸다(비트퍼펙트 표시는
    /// 내려간다). exclusive-strict 는 강등 대신 멈추고 사유를 남긴다 — 측정용.
    /// </summary>
    public string Backend { get; set; } = "exclusive";

    /// <summary>
    /// 마법사에서 고른 출력 장치 이름. 워커는 이걸 부분 일치로 찾는다.
    /// 비어 있으면 OS 기본 장치로 간다 — 고른 적이 없을 때의 옳은 동작이다.
    /// </summary>
    public string DeviceHint { get; set; } = "";

    /// <summary>
    /// 이 PC 에 달린 출력 목록(JSON). 열거는 NAudio 를 든 Output 워커가 하고,
    /// 여기서는 잠깐 띄워 stdout 한 줄을 받아 온다. 실패하면 빈 배열이다.
    /// </summary>
    public async Task<string> ListAudioDevicesAsync(CancellationToken ct = default)
    {
        var exe = FindExe("Mono.Output.exe", "Mono.Output");
        if (exe is null) return "[]";

        try
        {
            var psi = new ProcessStartInfo(exe, "--list-devices")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            };
            using var p = Process.Start(psi);
            if (p is null) return "[]";

            var json = await p.StandardOutput.ReadToEndAsync(ct);
            // 드라이버 하나가 열거 중에 얼면 여기서 끝없이 기다리게 된다. 목록은 그 정도 값어치가 아니다.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await p.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { p.Kill(entireProcessTree: true); } catch { /* 이미 죽었다 */ } }

            json = json.Trim();
            return json.StartsWith('[') ? json : "[]";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return "[]";
        }
    }

    public bool StartOutput(string? roomId, string host = "127.0.0.1")
    {
        var exe = FindExe("Mono.Output.exe", "Mono.Output");
        if (exe is null)
        {
            LastError = "Mono.Output.exe를 찾을 수 없습니다.";
            return false;
        }

        var args = BuildOutputArgs(roomId, host);

        // 룸 없이 띄운 Output 은 Core 에 엔드포인트로만 등록되고 어떤 룸에도 들어가지 않는다.
        // 나중에 룸이 생기면 그 룸으로 다시 띄워야 소리가 난다.
        //
        // 규격까지 같을 때만 그대로 둔다. 장치나 백엔드가 바뀌었는데 "이미 돌고 있다"고
        // 넘어가면, 설정에서 장치를 바꿔도 소리는 이전 장치로 계속 나면서 화면만 새 장치를
        // 가리킨다 — 사용자는 바뀐 줄 알고 있으므로 무엇이 틀렸는지 알아낼 방법이 없다.
        if (OutputRunning && OutputRoomId == roomId && _outputArgs == args) return true;

        var leavingAsio = OutputRunning && _outputArgs is not null && _outputArgs.Contains("--asio") && !args.Contains("--asio");
        var enteringAsio = OutputRunning && _outputArgs is not null && !_outputArgs.Contains("--asio") && args.Contains("--asio");
        if (OutputRunning) StopOutput();

        // ASIO 는 프로세스를 Kill 해도 드라이버 핸들이 한동안 장치에 남아 있다.
        // Fireface 처럼 배타 잠금이 끈질긴 장치에서 그 상태로 WASAPI 를 열면
        // 공유/배타 버퍼가 겹쳐 지직거린다. ASIO 쪽 ReleaseHold(3s) 와 맞춘다.
        if (leavingAsio || enteringAsio)
        {
            AppLog.Write("control", leavingAsio
                ? "ASIO → WASAPI: waiting for exclusive handle release"
                : "WASAPI → ASIO: waiting for endpoint release");
            Thread.Sleep(2800);
        }

        try
        {
            _outputIntentionallyStopped = false;
            _output = StartSilent(exe, args);
            _outputArgs = args;
            OutputRoomId = roomId;
            _outputHost = host;
            WatchOutput(_output);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>워커에 넘길 인자 한 줄. 재시작 판정도 이 문자열 비교로 한다.</summary>
    private string BuildOutputArgs(string? roomId, string host)
    {
        var args = $"--host={host}";
        if (!string.IsNullOrWhiteSpace(roomId)) args += $" --room={roomId}";
        if (Backend == "shared") args += " --shared";
        else if (Backend == "asio") args += " --asio";
        else if (Backend == "exclusive-strict") args += " --strict-exclusive";
        if (!string.IsNullOrWhiteSpace(DeviceHint)) args += $" --device=\"{DeviceHint}\"";
        return args;
    }

    public void StopOutput()
    {
        _outputIntentionallyStopped = true;
        OutputRoomId = null;
        _outputArgs = null;
        TryKill(ref _output);
    }

    /// <summary>
    /// 드라이버가 응답하지 않을 때 워커만 끊고 다시 세운다.
    /// 오디오 드라이버와 맞닿은 코드는 전부 이 프로세스 안에 있으므로,
    /// 여기만 재시작하면 OS 재부팅 없이 장치 제어권을 되찾는다.
    /// </summary>
    public bool RestartOutput()
    {
        var room = OutputRoomId;
        var host = _outputHost ?? "127.0.0.1";
        lock (_restarts) _restarts.Clear();   // 사용자가 직접 누른 재시도는 예산을 새로 준다
        _outputIntentionallyStopped = true;
        KillFast(ref _output);
        OutputRoomId = null;
        AppLog.Write("control", "restarting output worker");
        return StartOutput(room, host);
    }

    /// <summary>워커가 예기치 않게 죽으면 같은 룸으로 즉시 다시 세운다.</summary>
    private void WatchOutput(Process process)
    {
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                if (_outputIntentionallyStopped) return;

                var code = TryExitCode(process);

                if (!TakeRestartBudget())
                {
                    // 계속 죽는다면 다시 세워 봐야 같은 결과다. 폭주를 멈추고 사용자에게 넘긴다.
                    AppLog.Write("control", $"output worker keeps exiting (code={code}) — 재시작 중단");
                    OutputFaulted?.Invoke("출력 워커가 반복해서 종료됩니다. 장치 설정을 확인하세요.");
                    return;
                }

                AppLog.Write("control", $"output worker exited unexpectedly (code={code}) — 재시작");
                OutputFaulted?.Invoke("출력 워커가 예기치 않게 종료되어 다시 시작했습니다.");
                try { StartOutput(OutputRoomId, _outputHost ?? "127.0.0.1"); }
                catch (Exception ex) { LastError = ex.Message; }
            };
        }
        catch (Exception ex)
        {
            // 감시를 못 걸어도 재생 자체는 계속돼야 한다.
            AppLog.Write("control", "output watchdog 등록 실패: " + ex.Message);
        }
    }

    /// <summary>최근 창 안의 재기동 횟수를 세고 예산이 남았는지 본다.</summary>
    private bool TakeRestartBudget()
    {
        lock (_restarts)
        {
            var now = DateTimeOffset.UtcNow;
            while (_restarts.Count > 0 && now - _restarts.Peek() > RestartWindow) _restarts.Dequeue();
            if (_restarts.Count >= RestartBudget) return false;
            _restarts.Enqueue(now);
            return true;
        }
    }

    private static string TryExitCode(Process p)
    {
        try { return p.ExitCode.ToString(); }
        catch { return "?"; }
    }

    /// <summary>강제 종료를 CleanKillMs 안에 끝낸다. 드라이버가 얼어 있어도 UI 는 멈추지 않는다.</summary>
    private static void KillFast(ref Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(CleanKillMs);
            }
        }
        catch { /* 이미 죽었으면 무시 */ }
        finally
        {
            process?.Dispose();
            process = null;
        }
    }

    public void StopAll()
    {
        _outputIntentionallyStopped = true;
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
        // Control이 오래된 환경으로 떠 있어도 User 범위 MONO_* 는 Core/Output에 다시 실어 준다.
        InjectUserEnv(psi, "MONO_TIDAL_CLIENT_ID", "MONO_TIDAL_CLIENT_SECRET", "MONO_OAUTH_REDIRECT",
            "MONO_QOBUZ_APP_ID", "MONO_QOBUZ_APP_SECRET", "MONO_TIDAL_DEMO");
        var p = Process.Start(psi) ?? throw new InvalidOperationException("프로세스 기동 실패: " + exePath);

        // 파이프를 반드시 비워 준다. 읽지 않으면 4KB 버퍼가 차는 순간 자식이 Console.WriteLine 에서 멈춘다.
        var tag = Path.GetFileNameWithoutExtension(exePath);
        p.OutputDataReceived += (_, e) => AppLog.Write(tag, e.Data);
        p.ErrorDataReceived += (_, e) => AppLog.Write(tag, e.Data);
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        AppLog.Write("control", $"started {tag} {args}".TrimEnd());
        return p;
    }

    private static void InjectUserEnv(ProcessStartInfo psi, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User)
                        ?? Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Machine)
                        ?? Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value)) continue;
            psi.Environment[key] = value.Trim();
        }
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
