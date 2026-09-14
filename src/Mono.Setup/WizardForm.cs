using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace Mono.Setup;

/// <summary>
/// 설치만 하는 위저드. 설치 위치를 묻고, 풀고, 앱을 띄운다.
///
/// 예전에는 여기서 이름·라이브러리·출력 장치·스트리밍까지 물었다. 같은 질문을 앱의
/// 첫 실행 마법사가 훨씬 나은 화면으로 다시 하고 있었고, 두 답이 갈라지면 어느 쪽이
/// 이기는지도 분명하지 않았다. 설치 프로그램은 파일을 놓는 일만 한다.
/// </summary>
internal sealed class WizardForm : Form
{
    private static readonly Color CardPanel = Color.FromArgb(248, 255, 255, 255);
    private static readonly Color Accent = Color.FromArgb(109, 109, 246);
    private static readonly Color SoftAccent = Color.FromArgb(236, 236, 248);
    private static readonly Color TextCol = Color.FromArgb(26, 26, 30);
    private static readonly Color Muted = Color.FromArgb(107, 111, 122);

    private readonly bool _silent;
    private int _step;
    private string _installRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mono");
    private bool _busy;
    private Image? _bgImage;

    // 좌: 배경 히어로 / 우: UI 카드 (로고 PictureBox 없음 — 겹침 방지)
    private readonly Panel _stage = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 245, 247) };
    private readonly Panel _card = new()
    {
        Dock = DockStyle.Right,
        Width = 460,
        BackColor = CardPanel,
        Padding = new Padding(0),
    };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent, AutoScroll = true };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, Height = 128, BackColor = Color.Transparent };
    private ProgressBar? _bar;
    private Label? _status;

    public WizardForm(bool silent)
    {
        _silent = silent;
        Text = "mono";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1100, 720);
        BackColor = Color.FromArgb(245, 245, 247);
        ForeColor = TextCol;
        Font = new Font("Segoe UI", 10.5f);
        DoubleBuffered = true;
        MinimumSize = new Size(980, 680);

        _card.Controls.Add(_body);
        _card.Controls.Add(_footer);
        Controls.Add(_stage);
        Controls.Add(_card);

        Shown += async (_, _) =>
        {
            if (_silent)
            {
                _step = 1;
                Render();
                await InstallAndFinishAsync(launch: true, force: true);
                return;
            }

            Render();
        };
    }

    private string AppExe => Path.Combine(_installRoot, "current", "Mono.Control.exe");
    private bool AlreadyInstalled => File.Exists(AppExe);

    private void Render()
    {
        _body.SuspendLayout();
        _footer.SuspendLayout();
        _body.Controls.Clear();
        _footer.Controls.Clear();

        string bgName = _step == 0 ? "welcome" : "installing";
        var bg = LoadBg(bgName);
        if (bg is not null)
        {
            var old = _bgImage;
            _bgImage = bg;
            _stage.BackgroundImage = _bgImage;
            _stage.BackgroundImageLayout = ImageLayout.Zoom;
            old?.Dispose();
        }

        if (_step == 0) RenderWelcome();
        else RenderInstalling();

        _body.ResumeLayout();
        _footer.ResumeLayout();
    }

    private void RenderWelcome()
    {
        // 워드마크만 — 큰 앱 아이콘 PictureBox는 제거 (텍스트와 겹침)
        var brand = TitleLabel("mono", 36, 400);
        var tag = TitleLabel("혼자서도, 같이서도\n하나의 소리로.", 20, 400);
        var sub = MutedLabel("설치만 하면 됩니다.\n이름·라이브러리·출력 장치·스트리밍은 처음 실행할 때 앱 안에서 고릅니다.", 400);

        var pathCaption = MutedLabel("설치 위치", 400);
        var pathBox = new TextBox
        {
            Text = _installRoot,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Width = 280,
            Height = 34,
            BackColor = Color.White,
            ForeColor = TextCol,
            Font = new Font("Segoe UI", 10f),
        };
        var change = Pill("변경", SoftAccent, TextCol, 96);
        change.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "mono 설치 폴더",
                SelectedPath = Directory.Exists(_installRoot) ? _installRoot : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            };
            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            {
                _installRoot = Path.GetFileName(dlg.SelectedPath)
                    .Equals("Mono", StringComparison.OrdinalIgnoreCase)
                    ? dlg.SelectedPath
                    : Path.Combine(dlg.SelectedPath, "Mono");
                pathBox.Text = _installRoot;
            }
        };

        _body.Controls.Add(VStack(400, 28, brand, tag, sub, Gap(20), pathCaption, Row(12, pathBox, change)));

        var start = Pill(AlreadyInstalled ? "다시 설치" : "설치하기", Accent, Color.White, 200);
        start.Click += (_, _) =>
        {
            _step = 1;
            Render();
            _ = InstallAndFinishAsync(launch: true, force: true);
        };
        if (AlreadyInstalled)
        {
            var launch = Link("이미 설치됨 — 바로 실행");
            launch.Click += (_, _) => { LaunchApp(); Application.Exit(); };
            FooterStack(start, launch);
        }
        else
        {
            FooterCenter(start);
        }
    }

    private void RenderInstalling()
    {
        var title = TitleLabel("설치하는 중", 26, 400);
        var sub = MutedLabel("파일을 풀고 바로가기를 만든 뒤 mono를 준비합니다.", 400);
        _bar = new ProgressBar
        {
            Width = 320,
            Height = 10,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28,
        };
        _status = MutedLabel("잠시만 기다려 주세요…", 400);
        _body.Controls.Add(VStack(400, 48, title, sub, Gap(20), _bar, Gap(8), _status));
    }

    /// <param name="force">
    /// 이미 설치돼 있어도 페이로드를 다시 푼다. 사용자가 "다시 설치"를 눌렀거나
    /// --silent 로 특정 버전을 지정해 돌렸다면, 설치가 실제로 일어나야 한다.
    /// 그냥 띄우고 싶을 때는 환영 화면의 "바로 실행" 링크가 따로 있다.
    /// </param>
    private async Task InstallAndFinishAsync(bool launch, bool force = false)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            WritePrefs();

            if (force || !AlreadyInstalled)
            {
                if (_status is not null) _status.Text = "패키지 준비 중…";
                var setup = await Task.Run(ExtractPayload);
                if (setup is null)
                {
                    FailInstall("설치 패키지를 찾지 못했습니다.");
                    return;
                }

                TryUnblock(setup);
                if (_status is not null) _status.Text = "설치하는 중…";

                // Velopack 은 설치 루트를 제 것으로 보고 갈아엎는다. 그런데 그 루트 안에는
                // Core 의 data/(카탈로그·스트리밍 토큰·백업)와 prefs.ini 도 같이 산다.
                // 옆으로 빼 두지 않으면 "다시 설치" 한 번에 라이브러리와 로그인이 사라진다.
                var stash = StashUserData();
                int exitCode;
                try
                {
                    var psi = new ProcessStartInfo(setup)
                    {
                        Arguments = $"-s --installto \"{_installRoot}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var p = Process.Start(psi);
                    if (p is null)
                    {
                        FailInstall("설치 프로그램을 시작하지 못했습니다.");
                        return;
                    }

                    var done = await Task.Run(() => p.WaitForExit(5 * 60 * 1000));
                    if (!done)
                    {
                        try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                        FailInstall("설치가 끝나지 않았습니다.");
                        return;
                    }

                    exitCode = p.ExitCode;
                }
                finally
                {
                    // 설치가 실패해서 빠져나가더라도 사용자 데이터는 제자리로 돌려놓는다.
                    RestoreUserData(stash);
                }

                // 다시 설치일 때는 파일이 원래 있었으므로 존재 여부로는 성공을 알 수 없다.
                // 종료 코드를 봐야 "덮어썼다"와 "덮어쓰려다 실패했다"가 갈린다.
                if (exitCode != 0)
                {
                    FailInstall($"설치 프로그램이 오류로 끝났습니다 (코드 {exitCode}).");
                    return;
                }

                if (!AlreadyInstalled)
                {
                    FailInstall("설치가 끝나지 않았습니다.");
                    return;
                }
            }

            WritePrefs(); // again after install dir exists
            SaveGithubToken();
            if (_bar is not null)
            {
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Value = 100;
            }
            if (_status is not null) _status.Text = "설치되었습니다.";

            if (launch)
            {
                LaunchApp();
                Application.Exit();
            }
        }
        catch (Exception ex)
        {
            FailInstall(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// 설치 루트 안에서 Velopack 것이 아닌 항목. current·packages·Update.exe 는 설치가
    /// 다시 만들지만, 이것들은 사용자가 쌓은 것이라 지워지면 되돌릴 방법이 없다.
    /// </summary>
    private static readonly string[] PreservedEntries = ["data", "prefs.ini"];

    /// <summary>
    /// 설치 전에 사용자 데이터를 루트 밖으로 옮긴다. 같은 볼륨에 두어야 이동이
    /// 복사 없이 끝나므로, 임시 폴더가 아니라 설치 루트 바로 옆에 만든다.
    /// 옮길 것이 없으면 null.
    /// </summary>
    private string? StashUserData()
    {
        string? stash = null;
        foreach (var name in PreservedEntries)
        {
            var src = Path.Combine(_installRoot, name);
            var isDir = Directory.Exists(src);
            if (!isDir && !File.Exists(src)) continue;

            try
            {
                if (stash is null)
                {
                    var beside = Path.GetDirectoryName(_installRoot.TrimEnd(Path.DirectorySeparatorChar));
                    stash = Path.Combine(
                        string.IsNullOrEmpty(beside) ? Path.GetTempPath() : beside,
                        "mono-stash-" + Guid.NewGuid().ToString("n")[..8]);
                    Directory.CreateDirectory(stash);
                }

                var dest = Path.Combine(stash, name);
                if (isDir) Directory.Move(src, dest);
                else File.Move(src, dest);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 옮기지 못했으면 원래 자리에 남는다. 설치가 지울 수도 있지만,
                // 여기서 멈추면 설치 자체를 못 하므로 진행한다.
            }
        }

        return stash;
    }

    /// <summary>설치가 끝났거나 실패한 뒤 사용자 데이터를 제자리로 되돌린다.</summary>
    private void RestoreUserData(string? stash)
    {
        if (stash is null || !Directory.Exists(stash)) return;

        try { Directory.CreateDirectory(_installRoot); } catch (IOException) { return; }

        foreach (var name in PreservedEntries)
        {
            var src = Path.Combine(stash, name);
            var dest = Path.Combine(_installRoot, name);
            try
            {
                if (Directory.Exists(src))
                {
                    // 설치가 같은 이름으로 새로 만들어 뒀다면 빼 둔 쪽이 이긴다 —
                    // 그쪽이 사용자의 것이고, 새로 생긴 쪽은 빈 껍데기다.
                    if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
                    Directory.Move(src, dest);
                }
                else if (File.Exists(src))
                {
                    File.Move(src, dest, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 되돌리지 못한 것은 stash 폴더에 그대로 남겨 둔다. 지우는 것보다 낫다.
            }
        }

        // 전부 되돌렸을 때만 치운다. 남은 게 있으면 사용자가 찾아갈 수 있어야 한다.
        try
        {
            if (!Directory.EnumerateFileSystemEntries(stash).Any())
                Directory.Delete(stash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void FailInstall(string message)
    {
        if (_status is not null)
        {
            _status.ForeColor = Color.FromArgb(200, 60, 60);
            _status.Text = message;
        }
        var retry = Pill("다시 시도", Accent, Color.White, 160);
        retry.Click += (_, _) => { _step = 0; Render(); };
        FooterCenter(retry);
    }

    private void WritePrefs()
    {
        Directory.CreateDirectory(_installRoot);
        var prefs = Path.Combine(_installRoot, "prefs.ini");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(prefs))
        {
            foreach (var line in File.ReadAllLines(prefs))
            {
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                map[line[..i]] = line[(i + 1)..];
            }
        }

        // 나머지 설정은 첫 실행 마법사가 Core 의 setup.json 에 쓴다. 같은 값을 여기에도
        // 남기면 둘이 어긋났을 때 어느 쪽이 맞는지 알 수 없다.
        map["install_root"] = _installRoot;

        File.WriteAllLines(prefs, map.Select(kv => kv.Key + "=" + kv.Value));
    }

    private void SaveGithubToken()
    {
        try
        {
            var token = ReadGhToken()
                        ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                        ?? Environment.GetEnvironmentVariable("GH_TOKEN");
            if (string.IsNullOrWhiteSpace(token)) return;
            var prefs = Path.Combine(_installRoot, "prefs.ini");
            var lines = File.Exists(prefs) ? File.ReadAllLines(prefs).ToList() : [];
            lines.RemoveAll(l => l.StartsWith("github_token=", StringComparison.OrdinalIgnoreCase));
            lines.Add("github_token=" + token.Trim());
            File.WriteAllLines(prefs, lines);
        }
        catch { /* optional */ }
    }

    private void LaunchApp()
    {
        if (!File.Exists(AppExe)) return;
        Process.Start(new ProcessStartInfo(AppExe) { UseShellExecute = true });
    }

    private static void TryUnblock(string path)
    {
        try { File.Delete(path + ":Zone.Identifier"); } catch { /* ignore */ }
    }

    private static string? ReadGhToken()
    {
        try
        {
            var psi = new ProcessStartInfo("gh", "auth token")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            if (!p.WaitForExit(4000)) return null;
            var token = p.StandardOutput.ReadToEnd().Trim();
            return p.ExitCode == 0 && token.Length >= 8 ? token : null;
        }
        catch { return null; }
    }

    private static string? ExtractPayload()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "VelopackSetup.exe"),
                     Path.Combine(AppContext.BaseDirectory, "Payload", "VelopackSetup.exe"),
                 })
        {
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                return path;
        }

        using var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("VelopackSetup.exe");
        if (embedded is null || embedded.Length < 1024 * 1024) return null;
        var dir = Path.Combine(Path.GetTempPath(), "mono-setup-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "VelopackSetup.exe");
        using (var fs = File.Create(dest))
            embedded.CopyTo(fs);
        return dest;
    }

    private static Image? LoadLogo()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png");
        return stream is null ? null : Image.FromStream(stream);
    }

    private static Image? LoadBg(string stepName)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{stepName}.jpg");
        if (stream is not null) return Image.FromStream(stream);
        var directPath = Path.Combine(AppContext.BaseDirectory, "Assets", "backgrounds", $"{stepName}.jpg");
        return File.Exists(directPath) ? Image.FromFile(directPath) : null;
    }

    private void FooterCenter(Control c) => FooterStack(c);


    private void FooterStack(params Control[] items) => FooterActions(items);

    private void FooterActions(params Control[] items)
    {
        _footer.Controls.Clear();
        var inner = new Panel { BackColor = Color.Transparent, Width = Math.Max(200, _footer.ClientSize.Width) };
        var y = 16;
        foreach (var c in items)
        {
            if (c is Label lab)
                FitLabel(lab, Math.Min(520, Math.Max(200, _footer.ClientSize.Width - 48)));
            c.Location = new Point(Math.Max(0, (inner.Width - c.Width) / 2), y);
            inner.Controls.Add(c);
            y += c.Height + 10;
        }
        inner.Height = y + 16;
        void Place(object? s, EventArgs e)
        {
            inner.Width = Math.Max(200, _footer.ClientSize.Width);
            foreach (Control child in inner.Controls)
                child.Left = Math.Max(0, (inner.Width - child.Width) / 2);
            // 세로: 푸터 안에서 가운데, 아래 여백 확보해 잘림 방지
            inner.Location = new Point(0, Math.Max(8, (_footer.ClientSize.Height - inner.Height) / 2));
        }
        _footer.Resize += Place;
        Place(null, EventArgs.Empty);
        _footer.Controls.Add(inner);
    }

    private static Panel Row(int gap, params Control[] items)
    {
        var w = 0;
        var h = 0;
        foreach (var c in items)
        {
            h = Math.Max(h, c.Height);
            w += c.Width + gap;
        }
        w = Math.Max(0, w - gap);
        var p = new Panel { Width = w, Height = Math.Max(h, 40), BackColor = Color.Transparent };
        var x = 0;
        foreach (var c in items)
        {
            c.Location = new Point(x, Math.Max(0, (p.Height - c.Height) / 2));
            p.Controls.Add(c);
            x += c.Width + gap;
        }
        return p;
    }

    private Panel VStack(int width, int topPad, params Control[] items)
    {
        var host = new Panel
        {
            Width = width,
            BackColor = Color.Transparent,
            AutoSize = false,
        };

        var y = topPad;
        foreach (var c in items)
        {
            if (c is Label lab)
                FitLabel(lab, width);

            c.Location = new Point(Math.Max(0, (width - c.Width) / 2), y);
            host.Controls.Add(c);
            y += c.Height + 16;
        }

        host.Height = y + 16;

        void Place(object? s, EventArgs e)
        {
            var viewW = _body.ClientSize.Width;
            var viewH = _body.ClientSize.Height;
            // 가로·세로 모두 가운데. 넘치면 위에서 스크롤.
            var left = Math.Max(8, (viewW - width) / 2);
            var top = host.Height <= viewH - 8
                ? Math.Max(8, (viewH - host.Height) / 2)
                : 8;
            host.Location = new Point(left, top);
        }

        _body.Resize += Place;
        Place(null, EventArgs.Empty);
        return host;
    }

    private static void FitLabel(Label lab, int maxWidth)
    {
        var text = (lab.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        lab.Text = text.Replace("\n", Environment.NewLine);
        var boxW = lab.MaximumSize.Width > 0 ? Math.Min(lab.MaximumSize.Width, maxWidth) : maxWidth;
        boxW = Math.Max(120, boxW);

        var measured = TextRenderer.MeasureText(
            lab.Text,
            lab.Font,
            new Size(boxW, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

        var lineCount = Math.Max(1, lab.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length);
        if (lab.Text.Contains('\n') || lab.Text.Contains('\r'))
            lineCount = Math.Max(lineCount, lab.Text.Replace("\r\n", "\n").Split('\n').Length);

        var minH = (lab.Font.Height + 8) * lineCount;
        lab.AutoSize = false;
        lab.Width = boxW;
        lab.Height = Math.Max(measured.Height + 16, minH);
        lab.MaximumSize = new Size(boxW, 0);
        lab.TextAlign = ContentAlignment.TopCenter;
        lab.UseCompatibleTextRendering = false;
    }

    private static Control Gap(int h) => new Panel
    {
        Height = Math.Max(4, h),
        Width = 10,
        BackColor = Color.Transparent,
    };

    private static Label TitleLabel(string text, float size, int width) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", size, FontStyle.Bold),
        ForeColor = TextCol,
        AutoSize = false,
        Width = width,
        MaximumSize = new Size(width, 0),
        TextAlign = ContentAlignment.TopCenter,
    };

    private static Label MutedLabel(string text, int width = 520) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 10.5f),
        ForeColor = Muted,
        AutoSize = false,
        Width = width,
        MaximumSize = new Size(width, 0),
        TextAlign = ContentAlignment.TopCenter,
    };

    private static Button Pill(string text, Color fill, Color fg, int width)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(width, 44),
            FlatStyle = FlatStyle.Flat,
            BackColor = fill,
            ForeColor = fg,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(10, 0, 10, 0),
            UseCompatibleTextRendering = true,
        };
        b.FlatAppearance.BorderSize = 0;
        b.Region = RoundRegion(b.Width, b.Height, 22);
        return b;
    }

    private static LinkLabel Link(string text)
    {
        return new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Muted,
            ActiveLinkColor = Accent,
            VisitedLinkColor = Muted,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Font = new Font("Segoe UI", 10f),
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(0, 4, 0, 8),
        };
    }

    private static Region RoundRegion(int w, int h, int r)
    {
        var path = new GraphicsPath();
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(w - r, 0, r, r, 270, 90);
        path.AddArc(w - r, h - r, r, r, 0, 90);
        path.AddArc(0, h - r, r, r, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }
}
