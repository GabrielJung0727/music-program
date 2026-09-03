using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace Mono.Setup;

/// <summary>
/// Roon형 step 0–4 설치 위저드. 선택값은 prefs.ini에 쓰고 onboarded=1 로 남긴다.
/// </summary>
internal sealed class WizardForm : Form
{
    private static readonly Color Bg = Color.FromArgb(255, 255, 255);
    private static readonly Color TextCol = Color.FromArgb(26, 26, 30);
    private static readonly Color Muted = Color.FromArgb(107, 111, 122);
    private static readonly Color Accent = Color.FromArgb(109, 109, 246);
    private static readonly Color Border = Color.FromArgb(230, 231, 236);
    private static readonly Color CardBg = Color.FromArgb(247, 247, 250);
    private static readonly Color SoftAccent = Color.FromArgb(236, 236, 248);

    private readonly bool _silent;
    private int _step; // 0..4 config, 5 = installing
    private string _installRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mono");
    private string _displayName = Environment.UserName;
    private string _libraryPath = "";
    private string _zoneName = "This PC";
    private bool _enableLocalOutput = true;
    private string _streaming = "none"; // tidal | qobuz | demo | none
    private bool _busy;

    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = Bg };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, Height = 88, BackColor = Bg };
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
        ClientSize = new Size(820, 640);
        BackColor = Bg;
        ForeColor = TextCol;
        Font = new Font("Segoe UI", 10.5f);
        DoubleBuffered = true;
        MinimumSize = new Size(780, 600);

        Controls.Add(_body);
        Controls.Add(_footer);

        Shown += async (_, _) =>
        {
            if (_silent)
            {
                _step = 5;
                Render();
                await InstallAndFinishAsync(launch: true);
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

        switch (_step)
        {
            case 0: RenderWelcome(); break;
            case 1: RenderName(); break;
            case 2: RenderLibrary(); break;
            case 3: RenderAudio(); break;
            case 4: RenderStreaming(); break;
            default: RenderInstalling(); break;
        }

        _body.ResumeLayout();
        _footer.ResumeLayout();
    }

    private void RenderWelcome()
    {
        var logo = new PictureBox
        {
            Size = new Size(88, 88),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.None,
        };
        var img = LoadLogo();
        if (img is not null) logo.Image = img;

        var brand = TitleLabel("mono", 36);
        brand.Font = new Font("Segoe UI Semibold", 36f);
        var tag = TitleLabel("혼자서도, 같이서도\n하나의 소리로.", 22);
        tag.Font = new Font("Segoe UI Semibold", 20f);
        var sub = MutedLabel("이름·라이브러리·출력·스트리밍을 여기서 맞춘 뒤 설치합니다.\n본 앱을 열면 바로 쓸 수 있습니다.");

        var pathCaption = MutedLabel("설치 위치");
        var pathRow = new Panel { Height = 40, Width = 520 };
        var pathBox = new TextBox
        {
            Text = _installRoot,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Width = 400,
            Height = 32,
            Location = new Point(0, 4),
            BackColor = Color.White,
            ForeColor = TextCol,
        };
        var change = Pill("변경", SoftAccent, TextCol, 96);
        change.Location = new Point(416, 2);
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
        pathRow.Controls.Add(pathBox);
        pathRow.Controls.Add(change);

        var stack = CenterStack(560, logo, Gap(12), brand, Gap(8), tag, Gap(10), sub, Gap(28), pathCaption, Gap(6), pathRow);
        _body.Controls.Add(stack);

        var start = Pill(AlreadyInstalled ? "설정 계속" : "시작하기", Accent, Color.White, 200);
        start.Click += (_, _) => { _step = 1; Render(); };
        FooterCenter(start);

        if (AlreadyInstalled)
        {
            var launch = Link("이미 설치됨 — 바로 실행");
            launch.Click += (_, _) => { LaunchApp(); Application.Exit(); };
            launch.Location = new Point(24, 52);
            _footer.Controls.Add(launch);
        }
    }

    private void RenderName()
    {
        AddBack();
        var title = TitleLabel("어떻게 불러드릴까요?", 26);
        var sub = MutedLabel("라운지와 기기에서 보이는 표시 이름입니다.");
        var box = new TextBox
        {
            Text = _displayName,
            Width = 360,
            Height = 36,
            Font = new Font("Segoe UI", 12f),
            BorderStyle = BorderStyle.FixedSingle,
        };
        box.TextChanged += (_, _) => _displayName = box.Text;
        var stack = CenterStack(480, title, Gap(8), sub, Gap(24), box);
        _body.Controls.Add(stack);

        var next = Pill("계속", Accent, Color.White, 160);
        next.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_displayName))
                _displayName = Environment.UserName;
            _step = 2;
            Render();
        };
        FooterCenter(next);
    }

    private void RenderLibrary()
    {
        AddBack();
        var title = TitleLabel("음원 라이브러리", 26);
        var sub = MutedLabel("로컬 음악 폴더를 지정하면 첫 실행 때 스캔합니다. 나중에 Settings에서도 바꿀 수 있습니다.");
        var pathBox = new TextBox
        {
            Text = _libraryPath,
            Width = 400,
            Height = 32,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            PlaceholderText = "폴더 선택…",
        };
        var browse = Pill("폴더…", SoftAccent, TextCol, 100);
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "음악 라이브러리 폴더" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _libraryPath = dlg.SelectedPath;
                pathBox.Text = _libraryPath;
            }
        };
        var row = new Panel { Width = 520, Height = 40 };
        pathBox.Location = new Point(0, 4);
        browse.Location = new Point(412, 2);
        row.Controls.Add(pathBox);
        row.Controls.Add(browse);

        var stack = CenterStack(560, title, Gap(8), sub, Gap(24), row);
        _body.Controls.Add(stack);

        var skip = Link("건너뛰기");
        skip.Click += (_, _) => { _libraryPath = ""; _step = 3; Render(); };
        var next = Pill("계속", Accent, Color.White, 160);
        next.Click += (_, _) => { _step = 3; Render(); };
        FooterPair(skip, next);
    }

    private void RenderAudio()
    {
        AddBack();
        var title = TitleLabel("출력 장치", 26);
        var sub = MutedLabel("이 PC의 WASAPI/ASIO 출력을 쓰려면 Enable 하세요. 존 이름은 나중에 Audio에서 바꿀 수 있습니다.");

        var card = new Panel
        {
            Width = 560,
            Height = 88,
            BackColor = CardBg,
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        var icon = new Label
        {
            Text = "♪",
            Font = new Font("Segoe UI Semibold", 22f),
            ForeColor = Accent,
            Location = new Point(18, 22),
            AutoSize = true,
        };
        var name = new Label
        {
            Text = "System Output",
            Font = new Font("Segoe UI Semibold", 12f),
            ForeColor = TextCol,
            Location = new Point(64, 20),
            AutoSize = true,
        };
        var kind = new Label
        {
            Text = "WASAPI / ASIO · This PC",
            ForeColor = Muted,
            Location = new Point(64, 48),
            AutoSize = true,
        };
        var enable = Pill(_enableLocalOutput ? "Enabled" : "Enable",
            _enableLocalOutput ? Accent : SoftAccent,
            _enableLocalOutput ? Color.White : TextCol, 110);
        enable.Location = new Point(430, 24);
        enable.Click += (_, _) =>
        {
            _enableLocalOutput = !_enableLocalOutput;
            Render();
        };
        card.Controls.Add(icon);
        card.Controls.Add(name);
        card.Controls.Add(kind);
        card.Controls.Add(enable);

        var zoneCaption = MutedLabel("존 이름");
        var zone = new TextBox
        {
            Text = _zoneName,
            Width = 280,
            Height = 32,
            BorderStyle = BorderStyle.FixedSingle,
        };
        zone.TextChanged += (_, _) => _zoneName = zone.Text;

        var stack = CenterStack(580, title, Gap(8), sub, Gap(24), card, Gap(20), zoneCaption, Gap(6), zone);
        _body.Controls.Add(stack);

        var skip = Link("건너뛰기");
        skip.Click += (_, _) => { _enableLocalOutput = false; _step = 4; Render(); };
        var next = Pill("계속", Accent, Color.White, 160);
        next.Click += (_, _) => { _step = 4; Render(); };
        FooterPair(skip, next);
        var note = MutedLabel("걱정 마세요. Audio에서 언제든 장치를 추가할 수 있습니다.");
        note.Location = new Point((ClientSize.Width - 420) / 2, 58);
        note.AutoSize = true;
        _footer.Controls.Add(note);
    }

    private void RenderStreaming()
    {
        AddBack();
        var title = TitleLabel("스트리밍 연동", 26);
        var sub = MutedLabel("Tidal·Qobuz를 연결하거나 데모로 미리 볼 수 있습니다. 파트너 키가 없으면 데모 토큰을 씁니다.");

        Panel Card(string heading, string body, string choice)
        {
            var p = new Panel { Width = 170, Height = 200, BackColor = CardBg, Margin = new Padding(8) };
            p.Paint += (_, e) =>
            {
                var selected = _streaming == choice;
                using var pen = new Pen(selected ? Accent : Border, selected ? 2f : 1f);
                e.Graphics.DrawRectangle(pen, 1, 1, p.Width - 3, p.Height - 3);
            };
            var h = new Label
            {
                Text = heading,
                Font = new Font("Segoe UI Semibold", 12f),
                ForeColor = TextCol,
                Location = new Point(14, 16),
                AutoSize = true,
            };
            var b = new Label
            {
                Text = body,
                ForeColor = Muted,
                Location = new Point(14, 48),
                Size = new Size(142, 80),
            };
            var go = Pill("선택", SoftAccent, TextCol, 120);
            go.Location = new Point(25, 148);
            go.Click += (_, _) =>
            {
                _streaming = choice;
                _step = 5;
                Render();
                _ = InstallAndFinishAsync(launch: true);
            };
            p.Controls.Add(h);
            p.Controls.Add(b);
            p.Controls.Add(go);
            return p;
        }

        var row = new FlowLayoutPanel
        {
            Width = 580,
            Height = 220,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Bg,
        };
        row.Controls.Add(Card("TIDAL", "HiFi / Max 카탈로그를 라이브러리에 더합니다.", "tidal"));
        row.Controls.Add(Card("Qobuz", "Studio·Hi-Res 음원으로 확장합니다.", "qobuz"));
        row.Controls.Add(Card("데모", "키 없이 데모 토큰으로 미리 봅니다.", "demo"));

        var stack = CenterStack(600, title, Gap(8), sub, Gap(20), row);
        _body.Controls.Add(stack);

        var no = Link("나중에 — 설치만 진행");
        no.Click += (_, _) =>
        {
            _streaming = "none";
            _step = 5;
            Render();
            _ = InstallAndFinishAsync(launch: true);
        };
        FooterCenter(no);
        var note = MutedLabel("나중에 Settings에서도 연동할 수 있습니다.");
        note.Location = new Point((ClientSize.Width - 320) / 2, 58);
        note.AutoSize = true;
        _footer.Controls.Add(note);
    }

    private void RenderInstalling()
    {
        var title = TitleLabel("설치하는 중", 26);
        var sub = MutedLabel("파일을 풀고 바로가기를 만든 뒤 mono를 준비합니다.");
        _bar = new ProgressBar
        {
            Width = 420,
            Height = 10,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28,
        };
        _status = MutedLabel("잠시만 기다려 주세요…");
        _status.Width = 420;
        var stack = CenterStack(480, title, Gap(8), sub, Gap(28), _bar, Gap(12), _status);
        _body.Controls.Add(stack);
    }

    private async Task InstallAndFinishAsync(bool launch)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            WritePrefs();

            if (!AlreadyInstalled)
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

        map["onboarded"] = "1";
        map["name"] = string.IsNullOrWhiteSpace(_displayName) ? Environment.UserName : _displayName.Trim();
        map["library_path"] = _libraryPath.Trim();
        map["zone_name"] = string.IsNullOrWhiteSpace(_zoneName) ? "This PC" : _zoneName.Trim();
        map["connect_local_output"] = _enableLocalOutput ? "1" : "0";
        map["scan_library_on_start"] = string.IsNullOrWhiteSpace(_libraryPath) ? "0" : "1";
        map["streaming_choice"] = _streaming;
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

    private void AddBack()
    {
        var back = Link("← 뒤로");
        back.Location = new Point(24, 20);
        back.Click += (_, _) =>
        {
            if (_step > 0)
            {
                _step--;
                Render();
            }
        };
        _body.Controls.Add(back);
    }

    private void FooterCenter(Control c)
    {
        c.Location = new Point((ClientSize.Width - c.Width) / 2, 18);
        _footer.Controls.Add(c);
    }

    private void FooterPair(Control left, Control right)
    {
        var total = left.Width + 16 + right.Width;
        var x = (ClientSize.Width - total) / 2;
        left.Location = new Point(x, 22);
        right.Location = new Point(x + left.Width + 16, 18);
        _footer.Controls.Add(left);
        _footer.Controls.Add(right);
    }

    private Panel CenterStack(int width, params Control[] items)
    {
        var p = new Panel { Width = width, BackColor = Bg };
        var y = 0;
        foreach (var c in items)
        {
            c.Location = new Point(Math.Max(0, (width - c.Width) / 2), y);
            p.Controls.Add(c);
            y += c.Height + 4;
        }
        p.Height = y + 8;
        void LayoutCenter(object? s, EventArgs e)
        {
            p.Location = new Point(Math.Max(24, (_body.ClientSize.Width - p.Width) / 2),
                Math.Max(48, (_body.ClientSize.Height - p.Height) / 2 - 20));
        }
        _body.Resize += LayoutCenter;
        LayoutCenter(null, EventArgs.Empty);
        return p;
    }

    private static Control Gap(int h) => new Panel { Height = h, Width = 10, BackColor = Bg };

    private static Label TitleLabel(string text, float size) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", size),
        ForeColor = TextCol,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleCenter,
    };

    private static Label MutedLabel(string text) => new()
    {
        Text = text,
        ForeColor = Muted,
        AutoSize = true,
        MaximumSize = new Size(520, 0),
        TextAlign = ContentAlignment.MiddleCenter,
    };

    private static Button Pill(string text, Color fill, Color fg, int width)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(width, 40),
            FlatStyle = FlatStyle.Flat,
            BackColor = fill,
            ForeColor = fg,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 10.5f),
        };
        b.FlatAppearance.BorderSize = 0;
        // rounded feel via region
        b.Region = RoundRegion(b.Width, b.Height, 20);
        return b;
    }

    private static LinkLabel Link(string text)
    {
        var l = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Muted,
            ActiveLinkColor = Accent,
            VisitedLinkColor = Muted,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        return l;
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
