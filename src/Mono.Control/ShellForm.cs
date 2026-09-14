using System.ComponentModel;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Mono.Control;

/// <summary>
/// Control 창. 화면은 전부 src/Mono.Web 의 React 번들이고 Core 가 7702 에서 서빙한다.
/// 이 셸이 하는 일은 세 가지뿐 — Core·Output 프로세스 관리, WebView2 호스팅,
/// 그리고 브라우저가 못 하는 일(폴더 선택·업데이트·트레이)을 브리지로 열어 주는 것.
/// </summary>
public sealed class ShellForm : Form
{
    private const string BaseUrl = "http://127.0.0.1:7702";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Label _splash = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Mono 시작 중…",
        ForeColor = Color.FromArgb(0xF2, 0xF2, 0xF6),
        BackColor = Color.FromArgb(0x12, 0x13, 0x17),
        Font = new Font("Segoe UI", 11f),
    };

    private readonly ProcessSupervisor _supervisor = new();
    private readonly AppUpdater _updater = new();
    private readonly NotifyIcon _tray;
    private readonly CancellationTokenSource _life = new();
    private bool _quitting;

    public ShellForm()
    {
        Text = "Mono";
        MinimumSize = new Size(1100, 720);
        Size = new Size(1440, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x12, 0x13, 0x17);
        TryLoadIcon();

        Controls.Add(_web);
        Controls.Add(_splash);
        _web.Visible = false;

        _tray = new NotifyIcon
        {
            Text = "Mono",
            Visible = true,
            Icon = Icon ?? SystemIcons.Application,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add("열기", null, (_, _) => RestoreWindow());
        _tray.ContextMenuStrip.Items.Add("종료", null, (_, _) => QuitFromTray());
        _tray.DoubleClick += (_, _) => RestoreWindow();

        _supervisor.OutputFaulted += reason => BeginInvoke(() => PushEvent("output.faulted", new { reason }));

        Load += async (_, _) => await BootAsync();
    }

    // ── 기동 ────────────────────────────────────────────────────────────────

    private async Task BootAsync()
    {
        try
        {
            if (!await _supervisor.EnsureCoreAsync(_life.Token))
            {
                ShowFatal("Core를 시작하지 못했습니다.\n\n" + (_supervisor.LastError ?? "알 수 없는 오류"));
                return;
            }

            // Core 의 TCP 7700 은 떴어도 HTTP 7702 는 몇 백 ms 늦게 열린다.
            if (!await WaitForHttpAsync(_life.Token))
            {
                ShowFatal("Core가 응답하지 않습니다 (http://127.0.0.1:7702).\n\n로그: " + AppLog.FilePath);
                return;
            }

            await InitWebViewAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Program.LogDiagnostic("boot", ex);
            ShowFatal("시작 중 오류가 발생했습니다.\n\n" + ex.Message);
        }
    }

    private static async Task<bool> WaitForHttpAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var res = await http.GetAsync(BaseUrl + "/api/health", ct);
                if (res.IsSuccessStatusCode) return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }

            await Task.Delay(250, ct);
        }

        return false;
    }

    private async Task InitWebViewAsync()
    {
        // 사용자 데이터는 AppData 로. Program Files 밑에 쓰려다 권한으로 죽는 일을 막는다.
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mono", "webview");
        Directory.CreateDirectory(userData);

        CoreWebView2Environment env;
        try
        {
            env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        }
        catch (Exception ex)
        {
            Program.LogDiagnostic("webview2", ex);
            ShowFatal(
                "WebView2 런타임을 찾지 못했습니다.\n\n" +
                "Windows 11 에는 기본 포함돼 있습니다. 없다면 Microsoft Edge WebView2 Runtime 을 설치하세요.\n" +
                "https://developer.microsoft.com/microsoft-edge/webview2/");
            return;
        }

        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif

        core.WebMessageReceived += OnWebMessage;
        // 외부 링크(Tidal OAuth 등)는 기본 브라우저로 넘긴다. 앱 창이 로그인 페이지가 되면 안 된다.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(BaseUrl, StringComparison.OrdinalIgnoreCase)) return;
            if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            OpenExternal(e.Uri);
        };
        core.ProcessFailed += (_, e) =>
        {
            Program.LogDiagnostic("webview2", e.ProcessFailedKind + " " + e.Reason);
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                BeginInvoke(() => ShowFatal("브라우저 엔진이 종료됐습니다. Mono를 다시 실행하세요."));
        };

        // React 가 로드되기 전에 창에 window.mono 를 심는다.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);

        core.Navigate(BaseUrl + "/");
        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess) return;
            _splash.Visible = false;
            _web.Visible = true;
            _web.Focus();
        };
    }

    // ── 네이티브 브리지 ─────────────────────────────────────────────────────

    /// <summary>
    /// window.mono — React 에서 쓰는 얇은 RPC. 셸이 없으면(브라우저에서 열면)
    /// window.mono 자체가 없으므로, 웹 쪽은 항상 존재 여부를 먼저 본다.
    /// </summary>
    private const string BridgeScript = """
        (function () {
          const pending = new Map();
          let seq = 0;
          const listeners = new Map();

          window.chrome.webview.addEventListener('message', (e) => {
            const m = e.data;
            if (!m) return;
            if (m.event) {
              (listeners.get(m.event) || []).forEach(fn => { try { fn(m.data); } catch (_) {} });
              return;
            }
            const slot = pending.get(m.id);
            if (!slot) return;
            pending.delete(m.id);
            m.ok ? slot.resolve(m.result) : slot.reject(new Error(m.error || 'bridge error'));
          });

          function call(op, args) {
            const id = ++seq;
            return new Promise((resolve, reject) => {
              pending.set(id, { resolve, reject });
              window.chrome.webview.postMessage(Object.assign({ id, op }, args || {}));
              setTimeout(() => {
                if (pending.delete(id)) reject(new Error(op + ' timed out'));
              }, 120000);
            });
          }

          window.mono = {
            isShell: true,
            call,
            on(event, fn) {
              if (!listeners.has(event)) listeners.set(event, []);
              listeners.get(event).push(fn);
              return () => {
                const a = listeners.get(event) || [];
                const i = a.indexOf(fn);
                if (i >= 0) a.splice(i, 1);
              };
            },
            pickFolder: (title) => call('pickFolder', { title }),
            pickFile: (title, filter) => call('pickFile', { title, filter }),
            openExternal: (url) => call('openExternal', { url }),
            output: {
              start: (roomId, backend) => call('output.start', { roomId, backend }),
              stop: () => call('output.stop'),
              restart: () => call('output.restart'),
              status: () => call('output.status'),
            },
            update: {
              check: () => call('update.check'),
              apply: () => call('update.apply'),
            },
            app: {
              version: () => call('app.version'),
              logPath: () => call('app.logPath'),
              quit: () => call('app.quit'),
              minimizeToTray: () => call('app.minimizeToTray'),
            },
          };
        })();
        """;

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try
        {
            msg = JsonDocument.Parse(e.WebMessageAsJson).RootElement;
        }
        catch (JsonException)
        {
            return;
        }

        var id = msg.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
        var op = msg.TryGetProperty("op", out var opEl) ? opEl.GetString() ?? "" : "";

        try
        {
            var result = await DispatchAsync(op, msg);
            Reply(id, true, result, null);
        }
        catch (Exception ex)
        {
            Program.LogDiagnostic("bridge:" + op, ex);
            Reply(id, false, null, ex.Message);
        }
    }

    private async Task<object?> DispatchAsync(string op, JsonElement msg)
    {
        string? Str(string name) =>
            msg.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

        switch (op)
        {
            case "pickFolder":
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = Str("title") ?? "폴더 선택",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false,
                };
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
            }

            case "pickFile":
            {
                using var dlg = new OpenFileDialog
                {
                    Title = Str("title") ?? "파일 선택",
                    Filter = Str("filter") ?? "모든 파일|*.*",
                    CheckFileExists = true,
                };
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
            }

            case "openExternal":
                OpenExternal(Str("url"));
                return true;

            case "output.start":
            {
                var backend = Str("backend");
                if (!string.IsNullOrWhiteSpace(backend)) _supervisor.Backend = backend;
                var ok = _supervisor.StartOutput(Str("roomId"));
                return new { ok, error = ok ? null : _supervisor.LastError };
            }

            case "output.stop":
                _supervisor.StopOutput();
                return new { ok = true };

            case "output.restart":
            {
                var ok = _supervisor.RestartOutput();
                return new { ok, error = ok ? null : _supervisor.LastError };
            }

            case "output.status":
                return new
                {
                    running = _supervisor.OutputRunning,
                    roomId = _supervisor.OutputRoomId,
                    backend = _supervisor.Backend,
                    coreRunning = _supervisor.CoreRunning,
                };

            case "update.check":
            {
                var message = await _updater.CheckAsync();
                return new
                {
                    message,
                    available = _updater.Pending is not null,
                    version = _updater.Pending?.TargetFullRelease.Version.ToString(),
                    current = _updater.CurrentVersion,
                    installed = _updater.IsInstalled,
                };
            }

            case "update.apply":
                await _updater.DownloadAsync();
                _quitting = true;
                _updater.ApplyAndRestart();
                return true;

            case "app.version":
                return new { version = _updater.CurrentVersion, installed = _updater.IsInstalled };

            case "app.logPath":
                return AppLog.FilePath;

            case "app.minimizeToTray":
                Hide();
                return true;

            case "app.quit":
                BeginInvoke(QuitFromTray);
                return true;

            default:
                throw new NotSupportedException("알 수 없는 명령: " + op);
        }
    }

    private void Reply(int id, bool ok, object? result, string? error)
    {
        if (_web.CoreWebView2 is null) return;
        var payload = JsonSerializer.Serialize(new { id, ok, result, error });
        try { _web.CoreWebView2.PostWebMessageAsJson(payload); }
        catch (Exception ex) { Program.LogDiagnostic("bridge-reply", ex); }
    }

    private void PushEvent(string name, object? data)
    {
        if (_web.CoreWebView2 is null) return;
        try { _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { @event = name, data })); }
        catch (Exception ex) { Program.LogDiagnostic("bridge-event", ex); }
    }

    private static void OpenExternal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        // 기본 브라우저로 넘기는 건 http(s) 만. file:// 나 커스텀 스킴은 열지 않는다.
        if (uri.Scheme is not ("http" or "https")) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Program.LogDiagnostic("open-external", ex);
        }
    }

    // ── 창 수명 ─────────────────────────────────────────────────────────────

    private void ShowFatal(string message)
    {
        _web.Visible = false;
        _splash.Visible = true;
        _splash.Text = message;
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void QuitFromTray()
    {
        _quitting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // X 는 트레이로 내린다. 종료는 트레이 메뉴나 앱 안에서.
        if (!_quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _life.Cancel();
        _tray.Visible = false;
        _tray.Dispose();
        _supervisor.StopAll();
        base.OnFormClosed(e);
    }

    private void TryLoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "app", "mono-app.ico");
        try
        {
            if (File.Exists(path)) Icon = new Icon(path);
        }
        catch (Exception ex)
        {
            Program.LogDiagnostic("icon", ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _life.Dispose();
            _web.Dispose();
        }

        base.Dispose(disposing);
    }
}
