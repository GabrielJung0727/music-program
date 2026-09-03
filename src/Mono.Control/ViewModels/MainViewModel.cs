using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mono.Control.Models;
using Mono.Control.Services;
using Mono.Protocol;

namespace Mono.Control.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly CoreSession _session;
    private readonly ProcessSupervisor _supervisor;
    private readonly DispatcherTimer _clockTimer;
    private long _baseMedia;
    private long _baseLocal;
    private bool _playingClock;
    private CancellationTokenSource? _artCts;
    private string? _genreFilter;

    public MainViewModel(CoreSession session, ProcessSupervisor supervisor)
    {
        _session = session;
        _supervisor = supervisor;
        _session.MessageReceived += OnMessage;
        _session.ConnectionChanged += () => Dispatcher.UIThread.Post(() =>
        {
            IsConnected = _session.IsConnected;
            StatusText = IsConnected ? "Core 연결됨" : (_session.LastError ?? "연결 끊김");
        });

        NavItems =
        [
            new("home", "Home", "Browse"),
            new("genres", "Genres", "Browse"),
            new("qobuz", "Qobuz", "Browse"),
            new("tidal", "TIDAL", "Browse"),
            new("lounge", "라운지", "Browse"),
            new("history", "History", "Browse"),
            new("albums", "Albums", "My Library"),
            new("artists", "Artists", "My Library"),
            new("tracks", "Tracks", "My Library"),
            new("playlists", "Playlists", "My Library"),
            new("devices", "Audio", "Setup"),
            new("settings", "Settings", "Setup"),
        ];
        SelectedNav = NavItems[0];

        GenreTiles =
        [
            new("all", "All", "#2C2C34", "전체 라이브러리"),
            new("hires", "Hi-Res", "#1F4E5F", "96kHz+"),
            new("dsd", "DSD", "#5C3D2E", "네이티브 DSD"),
            new("jazz", "Jazz", "#3D4F5F", "시드·스캔 재즈"),
            new("tidal", "TIDAL", "#111111", "스트리밍"),
            new("qobuz", "Qobuz", "#1A3A5C", "Studio / Hi-Res"),
            new("local", "Local", "#3A4A3A", "로컬 파일"),
        ];

        CloseToTray = Prefs.GetBool("close_to_tray");
        LibraryPath = Prefs.Get("library_path", "");

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _clockTimer.Tick += (_, _) => TickClock();
        _clockTimer.Start();
    }

    public ObservableCollection<NavItem> NavItems { get; }
    public ObservableCollection<CatalogTrack> Tracks { get; } = new();
    public ObservableCollection<CatalogTrack> FilteredTracks { get; } = new();
    public ObservableCollection<HomeRail> HomeRails { get; } = new();
    public ObservableCollection<GenreTile> GenreTiles { get; }
    public ObservableCollection<RoomListItem> Rooms { get; } = new();
    public ObservableCollection<string> ChatLines { get; } = new();
    public ObservableCollection<CatalogTrack> QueueTracks { get; } = new();
    public ObservableCollection<CatalogTrack> AutoplayChoices { get; } = new();
    public ObservableCollection<string> LyricLines { get; } = new();

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private string _pageTitle = "Home";
    [ObservableProperty] private string _pageSubtitle = "Discover your library";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "시작하는 중…";
    [ObservableProperty] private bool _showOnboarding;
    [ObservableProperty] private int _onboardingStep;
    [ObservableProperty] private string _displayName = Environment.UserName;
    [ObservableProperty] private string _libraryPath = "";
    [ObservableProperty] private string _zoneName = "내 방";
    [ObservableProperty] private string _roomName = "Night Lounge";
    [ObservableProperty] private int _roomMode;
    [ObservableProperty] private string _joinRoomId = "";
    [ObservableProperty] private string _inviteCode = "";
    [ObservableProperty] private string _chatInput = "";
    [ObservableProperty] private string _nowTitle = "트랙을 큐에 넣으세요";
    [ObservableProperty] private string _nowArtist = "";
    [ObservableProperty] private string _nowBadge = "";
    [ObservableProperty] private string _nowArtUrl = "";
    [ObservableProperty] private Bitmap? _nowArt;
    [ObservableProperty] private string _roomChip = "룸 없음";
    [ObservableProperty] private string _syncText = "sync —";
    [ObservableProperty] private string _pathBadge = "";
    [ObservableProperty] private string _signalPathText = "Source → Core → Output";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _seekValue;
    [ObservableProperty] private double _seekMaximum = 1;
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private string _remainText = "0:00";
    [ObservableProperty] private bool _showAutoplay;
    [ObservableProperty] private string _currentRoomId = "";
    [ObservableProperty] private string _wikiText = "";
    [ObservableProperty] private string _linerNotes = "";
    [ObservableProperty] private string _creditsText = "";
    [ObservableProperty] private string _artistBio = "";
    [ObservableProperty] private string _currentLyric = "";
    [ObservableProperty] private bool _darkTheme;
    [ObservableProperty] private CatalogTrack? _selectedTrack;
    [ObservableProperty] private bool _showNowPlaying;
    [ObservableProperty] private int _nowPlayingTab;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _eqGraphicMode;
    [ObservableProperty] private double _eqBand1;
    [ObservableProperty] private double _eqBand2;
    [ObservableProperty] private double _eqBand3;
    [ObservableProperty] private double _eqBand4;
    [ObservableProperty] private double _eqBand5;
    [ObservableProperty] private string _irPath = "";
    [ObservableProperty] private double _speakerDelayL;
    [ObservableProperty] private double _speakerDelayR;
    [ObservableProperty] private double _speakerGainL;
    [ObservableProperty] private double _speakerGainR;
    [ObservableProperty] private double _headroomDb = -3;
    [ObservableProperty] private string _deviceEqProfile = "harman";
    [ObservableProperty] private string _syncProbeText = "";

    public bool IsLoungePage => SelectedNav?.Id == "lounge";
    public bool IsDevicesPage => SelectedNav?.Id == "devices";
    public bool IsSettingsPage => SelectedNav?.Id == "settings";
    public bool IsHomePage => SelectedNav?.Id == "home";
    public bool IsGenresPage => SelectedNav?.Id == "genres";
    public bool IsLibraryGrid => IsContentLibrary && !IsHomePage && !IsGenresPage;
    public bool IsLibraryToolsVisible => SelectedNav?.Id is "home" or "albums" or "artists" or "tracks" or "genres" or "qobuz" or "tidal";
    public bool IsContentLibrary => !IsLoungePage && !IsDevicesPage && !IsSettingsPage;
    public string PlayPauseLabel => IsPlaying ? "⏸" : "▶";
    public bool IsNpLyrics => NowPlayingTab == 0;
    public bool IsNpArtist => NowPlayingTab == 1;
    public bool IsNpCredits => NowPlayingTab == 2;
    public bool IsObStep0 => OnboardingStep == 0;
    public bool IsObStep1 => OnboardingStep == 1;
    public bool IsObStep2 => OnboardingStep == 2;
    public bool IsObStep3 => OnboardingStep == 3;
    public bool IsObStep4 => OnboardingStep == 4;
    public bool IsObStep5 => OnboardingStep == 5;

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayPauseLabel));
    partial void OnNowPlayingTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsNpLyrics));
        OnPropertyChanged(nameof(IsNpArtist));
        OnPropertyChanged(nameof(IsNpCredits));
    }
    partial void OnCloseToTrayChanged(bool value) => Prefs.SetBool("close_to_tray", value);
    partial void OnLibraryPathChanged(string value) => Prefs.Set("library_path", value ?? "");
    partial void OnOnboardingStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsObStep0));
        OnPropertyChanged(nameof(IsObStep1));
        OnPropertyChanged(nameof(IsObStep2));
        OnPropertyChanged(nameof(IsObStep3));
        OnPropertyChanged(nameof(IsObStep4));
        OnPropertyChanged(nameof(IsObStep5));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedNavChanged(NavItem? value)
    {
        OnPropertyChanged(nameof(IsLoungePage));
        OnPropertyChanged(nameof(IsDevicesPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsHomePage));
        OnPropertyChanged(nameof(IsGenresPage));
        OnPropertyChanged(nameof(IsLibraryGrid));
        OnPropertyChanged(nameof(IsLibraryToolsVisible));
        OnPropertyChanged(nameof(IsContentLibrary));
        if (value is null) return;
        PageTitle = value.Label switch
        {
            "Home" => "Home",
            "Albums" => "My Albums",
            "Artists" => "My Artists",
            "Tracks" => "My Tracks",
            "Playlists" => "My Playlists",
            "Genres" => "Genres",
            "라운지" => "라운지",
            "Audio" => "Audio devices",
            "Settings" => "Settings",
            _ => value.Label
        };
        PageSubtitle = value.Section;
        if (value.Id != "genres") _genreFilter = null;
        ApplyFilter();
        if (value.Id is "lounge") _ = Safe(() => _session.ListRoomsAsync());
        if (value.Id is "history") _ = Safe(() => _session.HistoryAsync());
        if (value.Id is "playlists") _ = Safe(() => _session.PlaylistsAsync());
        if (value.Id is "devices") _ = Safe(async () => { await _session.EndpointsAsync(); await _session.ListZonesAsync(); });
    }

    public async Task StartAsync()
    {
        ShowOnboarding = !Prefs.GetBool("onboarded");
        DisplayName = Prefs.Get("name", Environment.UserName);
        _session.DisplayName = DisplayName;

        StatusText = "Core 확인 중…";
        if (!await _supervisor.EnsureCoreAsync())
        {
            StatusText = _supervisor.LastError ?? "Core 기동 실패";
            return;
        }

        StatusText = "Core 연결 중…";
        if (!await _session.ConnectAsync())
        {
            StatusText = _session.LastError ?? "연결 실패";
            return;
        }

        await Safe(() => _session.CatalogAsync());
        await Safe(() => _session.ListRoomsAsync());
    }

    public async Task ShutdownAsync()
    {
        await _session.DisconnectAsync();
        _supervisor.StopAll();
    }

    public void SetLibraryPathFromPicker(string path)
    {
        LibraryPath = path;
        StatusText = "라이브러리 경로: " + path;
    }

    public void SetIrPathFromPicker(string path)
    {
        IrPath = path;
        _ = Safe(() => _session.SetConvolutionIrAsync(path));
    }

    [RelayCommand]
    private void SelectNav(NavItem? item)
    {
        if (item is not null) SelectedNav = item;
    }

    [RelayCommand]
    private void SelectGenre(GenreTile? tile)
    {
        if (tile is null) return;
        _genreFilter = tile.Id == "all" ? null : tile.Id;
        PageSubtitle = tile.Subtitle;
        ApplyFilter();
    }

    [RelayCommand]
    private async Task JoinListedRoomAsync(RoomListItem? room)
    {
        if (room is null) return;
        JoinRoomId = room.Id;
        await JoinRoomAsync();
    }

    [RelayCommand]
    private void NextOnboarding()
    {
        if (OnboardingStep == 1)
        {
            Prefs.Set("name", DisplayName.Trim().Length == 0 ? "listener" : DisplayName.Trim());
            _session.DisplayName = Prefs.Get("name", "listener");
        }
        OnboardingStep = Math.Min(5, OnboardingStep + 1);
    }

    [RelayCommand]
    private void SkipOnboarding() => OnboardingStep = Math.Min(5, OnboardingStep + 1);

    [RelayCommand]
    private void FinishOnboarding()
    {
        Prefs.SetBool("onboarded", true);
        ShowOnboarding = false;
    }

    [RelayCommand]
    private void OpenGuide()
    {
        OnboardingStep = 0;
        ShowOnboarding = true;
    }

    [RelayCommand]
    private async Task ScanLibraryAsync() => await Safe(() => _session.ScanAsync(string.IsNullOrWhiteSpace(LibraryPath) ? null : LibraryPath));

    [RelayCommand]
    private async Task CreateZoneAsync()
    {
        if (string.IsNullOrWhiteSpace(ZoneName)) return;
        await Safe(() => _session.CreateZoneAsync(ZoneName.Trim()));
    }

    [RelayCommand]
    private async Task LinkTidalAsync() => await Safe(() => _session.BeginStreamingOAuthAsync(1));

    [RelayCommand]
    private async Task LinkQobuzAsync() => await Safe(() => _session.BeginStreamingOAuthAsync(2));

    [RelayCommand]
    private async Task LinkTidalDemoAsync() => await Safe(() => _session.LinkStreamingAsync(1));

    [RelayCommand]
    private async Task LinkQobuzDemoAsync() => await Safe(() => _session.LinkStreamingAsync(2));

    [RelayCommand]
    private async Task ConnectOutputAsync()
    {
        if (!_supervisor.StartOutput(string.IsNullOrWhiteSpace(CurrentRoomId) ? null : CurrentRoomId))
            StatusText = _supervisor.LastError ?? "Output 기동 실패";
        else
            StatusText = "Output 연결됨 (소리)";
    }

    [RelayCommand]
    private async Task PlayPauseAsync()
    {
        if (IsPlaying) await Safe(() => _session.PauseAsync());
        else await Safe(() => _session.PlayAsync());
    }

    [RelayCommand]
    private async Task PrevAsync() => await Safe(() => _session.SkipAsync(-1));

    [RelayCommand]
    private async Task NextAsync() => await Safe(() => _session.SkipAsync(1));

    [RelayCommand]
    private async Task ResyncAsync() => await Safe(() => _session.ResyncAsync());

    [RelayCommand]
    private async Task SyncProbeAsync() => await Safe(() => _session.SyncProbeAsync());

    [RelayCommand]
    private async Task CreateRoomAsync() => await Safe(() => _session.CreateRoomAsync(RoomName, RoomMode));

    [RelayCommand]
    private async Task JoinRoomAsync()
    {
        if (string.IsNullOrWhiteSpace(JoinRoomId)) return;
        await Safe(() => _session.JoinRoomAsync(JoinRoomId.Trim(), string.IsNullOrWhiteSpace(InviteCode) ? null : InviteCode.Trim()));
    }

    [RelayCommand]
    private async Task LeaveRoomAsync() => await Safe(() => _session.LeaveRoomAsync());

    [RelayCommand]
    private async Task RefreshRoomsAsync() => await Safe(() => _session.ListRoomsAsync());

    [RelayCommand]
    private async Task SendChatAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;
        var t = ChatInput.Trim();
        ChatInput = "";
        await Safe(() => _session.ChatAsync(t));
    }

    [RelayCommand]
    private async Task ReactAsync(string? emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return;
        await Safe(() => _session.ReactAsync(emoji));
    }

    [RelayCommand]
    private async Task PlayTrackAsync(CatalogTrack? track)
    {
        if (track is null) return;
        await Safe(async () =>
        {
            if (string.IsNullOrWhiteSpace(CurrentRoomId))
                await _session.CreateRoomAsync(RoomName, RoomMode);
            await _session.EnqueueAsync(track.Id);
            await _session.PlayAsync();
        });
    }

    [RelayCommand]
    private async Task EnqueueTrackAsync(CatalogTrack? track)
    {
        if (track is null) return;
        await Safe(() => _session.EnqueueAsync(track.Id));
    }

    [RelayCommand]
    private async Task ChooseAutoplayAsync(CatalogTrack? track)
    {
        if (track is null) return;
        await Safe(() => _session.ChooseAutoplayAsync(track.Id));
    }

    [RelayCommand]
    private async Task ClearQueueAsync() => await Safe(() => _session.ClearQueueAsync());

    [RelayCommand]
    private async Task SetDspAsync(string? preset)
    {
        if (!int.TryParse(preset, out var p)) return;
        await Safe(() => _session.SetDspAsync(p));
    }

    [RelayCommand]
    private async Task ApplyEasyEqAsync()
    {
        var bands = new[]
        {
            new { f = 60f, g = (float)EqBand1, q = 0.7f },
            new { f = 250f, g = (float)EqBand2, q = 0.9f },
            new { f = 1000f, g = (float)EqBand3, q = 1.0f },
            new { f = 4000f, g = (float)EqBand4, q = 1.1f },
            new { f = 12000f, g = (float)EqBand5, q = 0.8f },
        };
        var json = JsonSerializer.Serialize(bands);
        await Safe(() => _session.SetEasyEqAsync(json, EqGraphicMode));
        StatusText = EqGraphicMode ? "Graphic EQ 적용" : "Parametric EQ 적용";
    }

    [RelayCommand]
    private async Task ApplySpeakerSetupAsync()
    {
        var csv = string.Join(",",
            SpeakerDelayL.ToString(CultureInfo.InvariantCulture),
            SpeakerDelayR.ToString(CultureInfo.InvariantCulture),
            SpeakerGainL.ToString(CultureInfo.InvariantCulture),
            SpeakerGainR.ToString(CultureInfo.InvariantCulture));
        await Safe(() => _session.SetSpeakerSetupAsync(csv));
    }

    [RelayCommand]
    private async Task ApplyHeadroomAsync() => await Safe(() => _session.SetHeadroomAsync((float)HeadroomDb));

    [RelayCommand]
    private async Task ApplyDeviceEqAsync(string? profile)
    {
        var p = string.IsNullOrWhiteSpace(profile) ? DeviceEqProfile : profile!;
        DeviceEqProfile = p;
        await Safe(() => _session.SetDeviceEqAsync(p));
    }

    [RelayCommand]
    private async Task ClearIrAsync()
    {
        IrPath = "";
        await Safe(() => _session.SetConvolutionIrAsync(null));
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        DarkTheme = !DarkTheme;
        if (Avalonia.Application.Current is not null)
            Avalonia.Application.Current.RequestedThemeVariant =
                DarkTheme ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
    }

    [RelayCommand]
    private void OpenNowPlaying() => ShowNowPlaying = true;

    [RelayCommand]
    private void CloseNowPlaying() => ShowNowPlaying = false;

    [RelayCommand]
    private void SetNowPlayingTab(string? tab)
    {
        if (int.TryParse(tab, out var t)) NowPlayingTab = Math.Clamp(t, 0, 2);
    }

    [RelayCommand]
    private async Task SeekToAsync() => await Safe(() => _session.SeekAsync((long)SeekValue));

    private void OnMessage(MonoMessage msg) => Dispatcher.UIThread.Post(() => HandleMessage(msg));

    private void HandleMessage(MonoMessage msg)
    {
        if (!string.IsNullOrWhiteSpace(msg.Error))
            StatusText = msg.Error!;

        switch (msg.Type)
        {
            case MessageTypes.Welcome:
                StatusText = "환영합니다";
                break;
            case MessageTypes.Catalog:
            case MessageTypes.Search:
                LoadCatalog(msg.Body);
                break;
            case MessageTypes.ListRooms:
                LoadRooms(msg.Body);
                break;
            case MessageTypes.RoomState:
                ApplyRoomState(msg.Body);
                break;
            case MessageTypes.WikiBio:
                WikiText = msg.Body ?? msg.Text ?? "";
                ArtistBio = WikiText;
                break;
            case MessageTypes.Chat:
                if (!string.IsNullOrWhiteSpace(msg.Text))
                    ChatLines.Add($"{msg.DisplayName ?? msg.PeerId}: {msg.Text}");
                break;
            case MessageTypes.LinkStreaming:
                StatusText = (msg.Ok ?? false) ? "스트리밍 연동 응답" : (msg.Error ?? "스트리밍");
                if (!string.IsNullOrWhiteSpace(msg.Body) && msg.Body.Contains("authUrl", StringComparison.OrdinalIgnoreCase))
                    StatusText = "OAuth 브라우저 열림 — 데모면 ‘데모 토큰’으로 완료";
                break;
            case MessageTypes.SyncProbe:
                SyncProbeText = msg.Body ?? "";
                StatusText = "Sync probe 수신";
                break;
        }
    }

    private void LoadCatalog(string? body)
    {
        Tracks.Clear();
        if (string.IsNullOrWhiteSpace(body)) { ApplyFilter(); return; }
        try
        {
            var list = JsonSerializer.Deserialize<List<CatalogTrack>>(body, Json) ?? [];
            foreach (var t in list) Tracks.Add(t);
        }
        catch { /* ignore malformed */ }
        ApplyFilter();
        PageSubtitle = $"{Tracks.Count} tracks";
        _ = PrefetchArtAsync();
    }

    private async Task PrefetchArtAsync()
    {
        _artCts?.Cancel();
        _artCts = new CancellationTokenSource();
        var ct = _artCts.Token;
        foreach (var t in Tracks.ToList())
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(t.AbsoluteArtUrl)) continue;
            var bmp = await ArtCache.GetAsync(t.AbsoluteArtUrl, ct);
            if (bmp is not null)
                await Dispatcher.UIThread.InvokeAsync(() => t.Cover = bmp);
        }
    }

    private void LoadRooms(string? body)
    {
        Rooms.Clear();
        if (string.IsNullOrWhiteSpace(body)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<RoomListItem>>(body, Json) ?? [];
            foreach (var r in list) Rooms.Add(r);
        }
        catch { /* ignore */ }
    }

    private void ApplyRoomState(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        try
        {
            var node = JsonNode.Parse(body)?.AsObject();
            if (node is null) return;

            CurrentRoomId = node["id"]?.GetValue<string>() ?? "";
            RoomChip = $"{node["name"]?.GetValue<string>() ?? "룸"} · {CurrentRoomId[..Math.Min(6, CurrentRoomId.Length)]}";
            IsPlaying = node["playing"]?.GetValue<bool>() ?? false;
            PathBadge = node["pathBadge"]?.GetValue<string>()
                        ?? ((node["bitPerfect"]?.GetValue<bool>() ?? false) ? "Bit-perfect" : "Processed");
            NowBadge = PathBadge;

            var dsp = node["dspPreset"]?.ToString() ?? "Off";
            var dspOn = node["dspEnabled"]?.GetValue<bool>() ?? false;
            var ir = node["convolutionIrPath"]?.GetValue<string>();
            var deviceEq = node["deviceEqProfile"]?.GetValue<string>();
            SignalPathText = dspOn
                ? $"Decode → DSP({dsp}{(string.IsNullOrWhiteSpace(deviceEq) ? "" : "/" + deviceEq)}{(string.IsNullOrWhiteSpace(ir) ? "" : "+IR")}) → Output · {PathBadge}"
                : $"Decode → Bit-perfect → Output · {PathBadge}";
            if (!string.IsNullOrWhiteSpace(ir)) IrPath = ir;

            var media = node["mediaTimeMs"]?.GetValue<long>() ?? 0;
            var duration = Math.Max(1, node["durationMs"]?.GetValue<long>() ?? 1);
            SeekMaximum = duration;
            _baseMedia = media;
            _baseLocal = Environment.TickCount64;
            _playingClock = IsPlaying;
            SeekValue = media;
            UpdateTimeTexts(media, duration);

            LinerNotes = node["linerNotes"]?.GetValue<string>() ?? "";
            CreditsText = node["credits"]?.GetValue<string>() ?? "";
            CurrentLyric = node["currentLyric"]?.GetValue<string>() ?? "";
            LyricLines.Clear();
            if (node["lyrics"] is JsonArray ly)
            {
                foreach (var line in ly)
                {
                    var text = line?["text"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(text)) LyricLines.Add(text!);
                }
            }

            if (node["currentTrack"] is JsonObject ct)
            {
                NowTitle = ct["title"]?.GetValue<string>() ?? "트랙";
                NowArtist = string.Join(" · ", new[]
                {
                    ct["artistName"]?.GetValue<string>(),
                    ct["albumTitle"]?.GetValue<string>()
                }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var art = ct["artUrl"]?.GetValue<string>();
                NowArtUrl = string.IsNullOrWhiteSpace(art) ? "" : "http://127.0.0.1:7702" + art;
                _ = RefreshNowArtAsync(NowArtUrl);
                var artistId = ct["artistId"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(artistId) && string.IsNullOrWhiteSpace(ArtistBio))
                    _ = Safe(() => _session.WikiAsync(artistId!));
            }

            if (node["artist"] is JsonObject ar)
                ArtistBio = ar["bio"]?.GetValue<string>() ?? ArtistBio;

            QueueTracks.Clear();
            if (node["queue"] is JsonArray qArr)
            {
                foreach (var q in qArr)
                {
                    QueueTracks.Add(new CatalogTrack
                    {
                        Id = q?["trackId"]?.GetValue<string>() ?? "",
                        Title = q?["title"]?.GetValue<string>() ?? "Track",
                        Artist = q?["artist"]?.GetValue<string>(),
                        DurationMs = q?["durationMs"]?.GetValue<long>() ?? 0,
                        Badge = q?["badge"]?.GetValue<string>(),
                        ArtUrl = q?["artUrl"]?.GetValue<string>()
                    });
                }
            }

            if (node["members"] is JsonArray members)
            {
                foreach (var m in members)
                {
                    var stats = m?["stats"];
                    if (stats is null) continue;
                    var off = stats["offsetMs"]?.GetValue<double>();
                    var jit = stats["jitterMs"]?.GetValue<double>();
                    if (off is not null)
                    {
                        SyncText = $"sync {off:+0.00;-0.00}ms · jitter {jit:0.00}ms";
                        break;
                    }
                }
            }

            ChatLines.Clear();
            if (node["chat"] is JsonArray chat)
            {
                foreach (var c in chat)
                    ChatLines.Add($"{c?["peerName"]}: {c?["text"]}");
            }

            AutoplayChoices.Clear();
            ShowAutoplay = false;
            if (node["autoplay"] is JsonObject ap && ap["candidates"] is JsonArray cands)
            {
                ShowAutoplay = true;
                foreach (var c in cands)
                {
                    AutoplayChoices.Add(new CatalogTrack
                    {
                        Id = c?["id"]?.GetValue<string>() ?? "",
                        Title = c?["title"]?.GetValue<string>() ?? "",
                        Artist = c?["artist"]?.GetValue<string>() ?? c?["artistName"]?.GetValue<string>()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = "스냅샷 파싱: " + ex.Message;
        }
    }

    private async Task RefreshNowArtAsync(string url)
    {
        var bmp = await ArtCache.GetAsync(url);
        await Dispatcher.UIThread.InvokeAsync(() => NowArt = bmp);
    }

    private void TickClock()
    {
        if (!_playingClock || SeekMaximum <= 1) return;
        var media = _baseMedia + (Environment.TickCount64 - _baseLocal);
        if (media > SeekMaximum) media = (long)SeekMaximum;
        SeekValue = media;
        UpdateTimeTexts(media, (long)SeekMaximum);
    }

    private void UpdateTimeTexts(long media, long duration)
    {
        ElapsedText = TimeSpan.FromMilliseconds(media).ToString(@"m\:ss");
        RemainText = TimeSpan.FromMilliseconds(Math.Max(0, duration - media)).ToString(@"m\:ss");
    }

    private void ApplyFilter()
    {
        FilteredTracks.Clear();
        IEnumerable<CatalogTrack> src = Tracks;
        var nav = SelectedNav?.Id ?? "tracks";
        if (nav is "albums")
            src = Tracks.GroupBy(t => t.AlbumId ?? t.Album).Select(g => g.First());
        else if (nav is "artists")
            src = Tracks.GroupBy(t => t.ArtistId ?? t.Artist).Select(g => g.First());
        else if (nav is "qobuz")
            src = Tracks.Where(t => t.Source == 2 || t.GenreHint.Contains("Qobuz", StringComparison.OrdinalIgnoreCase));
        else if (nav is "tidal")
            src = Tracks.Where(t => t.Source == 1 || t.GenreHint.Contains("Tidal", StringComparison.OrdinalIgnoreCase));
        else if (nav is "genres" && _genreFilter is not null)
            src = FilterByGenre(Tracks, _genreFilter);

        var q = SearchText.Trim();
        if (q.Length > 0)
        {
            src = src.Where(t =>
                (t.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Artist?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Album?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = src.Take(500).ToList();
        foreach (var t in list) FilteredTracks.Add(t);

        RebuildHomeRails();
    }

    private static IEnumerable<CatalogTrack> FilterByGenre(IEnumerable<CatalogTrack> tracks, string id) => id switch
    {
        "hires" => tracks.Where(t => t.SampleRate >= 96000 && !t.IsDsd),
        "dsd" => tracks.Where(t => t.IsDsd),
        "jazz" => tracks.Where(t =>
            (t.Artist?.Contains("Coltrane", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (t.Artist?.Contains("Miles", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (t.Artist?.Contains("Brubeck", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (t.Artist?.Contains("Hiromi", StringComparison.OrdinalIgnoreCase) ?? false)),
        "tidal" => tracks.Where(t => t.Source == 1),
        "qobuz" => tracks.Where(t => t.Source == 2),
        "local" => tracks.Where(t => t.Source == 0),
        _ => tracks
    };

    private void RebuildHomeRails()
    {
        HomeRails.Clear();
        if (Tracks.Count == 0) return;
        HomeRails.Add(new HomeRail("Recently added", Tracks.Take(12)));
        HomeRails.Add(new HomeRail("Albums", Tracks.GroupBy(t => t.AlbumId ?? t.Album).Select(g => g.First()).Take(12)));
        HomeRails.Add(new HomeRail("Hi-Res & DSD", Tracks.Where(t => t.IsDsd || t.SampleRate >= 96000).Take(12)));
        HomeRails.Add(new HomeRail("Streaming", Tracks.Where(t => t.Source is 1 or 2).Take(12)));
    }

    private async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { StatusText = ex.Message; }
    }
}

internal static class Prefs
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mono");
    private static string FilePath => Path.Combine(Dir, "prefs.ini");

    private static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(FilePath)) return d;
        foreach (var line in File.ReadAllLines(FilePath))
        {
            var i = line.IndexOf('=');
            if (i <= 0) continue;
            d[line[..i]] = line[(i + 1)..];
        }
        return d;
    }

    private static void Save(Dictionary<string, string> d)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllLines(FilePath, d.Select(kv => kv.Key + "=" + kv.Value));
    }

    public static string Get(string key, string fallback)
    {
        var d = Load();
        return d.TryGetValue(key, out var v) ? v : fallback;
    }

    public static void Set(string key, string value)
    {
        var d = Load();
        d[key] = value;
        Save(d);
    }

    public static bool GetBool(string key) => Get(key, "") == "1";
    public static void SetBool(string key, bool value) => Set(key, value ? "1" : "0");
}
