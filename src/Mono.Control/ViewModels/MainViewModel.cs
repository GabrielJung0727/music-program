using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mono.Control.Models;
using Mono.Control.Services;
using Mono.Control.ViewModels.Pages;
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Control.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly CoreSession _session;
    private readonly ProcessSupervisor _supervisor;
    private readonly AppUpdater _updater = new();
    private readonly DispatcherTimer _clockTimer;
    private long _baseMedia;
    private long _baseLocal;
    private bool _playingClock;
    private CancellationTokenSource? _artCts;
    private string? _genreFilter;
    private CancellationTokenSource? _searchCts;

    public MainViewModel(CoreSession session, ProcessSupervisor supervisor)
    {
        _session = session;
        _supervisor = supervisor;
        Lounge = new LoungeViewModel(session);
        Lounge.SelfPeerId = session.PeerId;
        Audio = new AudioViewModel(session);
        Library = new LibraryViewModel(session);
        _session.MessageReceived += OnMessage;
        _session.ConnectionChanged += () => Dispatcher.UIThread.Post(() =>
        {
            IsConnected = _session.IsConnected;
            StatusText = IsConnected ? "Core 연결됨" : (_session.LastError ?? "연결 끊김");
        });

        NavItems =
        [
            new("home", "Home", "Browse", "nav-home"),
            new("genres", "Genres", "Browse", "nav-genres"),
            new("qobuz", "Qobuz", "Browse", "nav-qobuz"),
            new("tidal", "TIDAL", "Browse", "nav-tidal"),
            new("lounge", "라운지", "Browse", "nav-lounge"),
            new("history", "History", "Browse", "nav-history"),
            new("albums", "Albums", "My Library", "nav-albums"),
            new("artists", "Artists", "My Library", "nav-artists"),
            new("tracks", "Tracks", "My Library", "nav-tracks"),
            new("playlists", "Playlists", "My Library", "nav-playlists"),
            new("composers", "Composers", "My Library", "nav-artists"),
            new("compositions", "Compositions", "My Library", "nav-tracks"),
            new("folders", "Folders", "My Library", "nav-albums"),
            new("devices", "Audio", "Setup", "nav-audio"),
            new("settings", "Settings", "Setup", "nav-settings"),
        ];
        SelectedNav = NavItems[0];


        CloseToTray = Prefs.GetBool("close_to_tray");
        LibraryPath = Prefs.Get("library_path", "");
        DarkTheme = Prefs.GetBool("dark_theme");
        ApplyTheme();
        AppVersion = _updater.CurrentVersion;
        UpdateStatus = "";

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _clockTimer.Tick += (_, _) => TickClock();
        _clockTimer.Start();
    }

    /// <summary>화면별 상태. 셸은 스냅샷을 받아 이들에게 밀어 넣는다.</summary>
    public LoungeViewModel Lounge { get; }
    public AudioViewModel Audio { get; }
    public LibraryViewModel Library { get; }

    public ObservableCollection<NavItem> NavItems { get; }
    public ObservableCollection<CatalogTrack> Tracks { get; } = new();
    public ObservableCollection<CatalogTrack> FilteredTracks { get; } = new();
    public ObservableCollection<HomeRail> HomeRails { get; } = new();
    public ObservableCollection<OutputDevice> Outputs { get; } = new();
    public ObservableCollection<ZoneItem> Zones { get; } = new();
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
    [ObservableProperty] private string _appVersion = "";
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _updateBusy;
    [ObservableProperty] private bool _updateReady;
    [ObservableProperty] private int _updateProgress;
    [ObservableProperty] private bool _followHostView;
    [ObservableProperty] private double _linerScrollY;
    /// <summary>Core가 마지막으로 보낸 룸 스냅샷 — 페이지 뷰모델이 읽는 단일 상태원.</summary>
    [ObservableProperty] private RoomSnapshot? _currentSnapshot;
    [ObservableProperty] private IReadOnlyList<SnapshotHeatBucket> _heatmap = [];
    [ObservableProperty] private IReadOnlyList<SnapshotPin> _pins = [];
    [ObservableProperty] private bool _seekingAllowed = true;
    [ObservableProperty] private int _volume = 100;
    [ObservableProperty] private bool _volumeEnabled = true;
    [ObservableProperty] private string _volumeBlockedReason = "";
    [ObservableProperty] private OutputDevice? _selectedOutput;
    [ObservableProperty] private string _outputChip = "출력 없음";
    [ObservableProperty] private int _heartCount;
    [ObservableProperty] private int _heartsLeft = 3;
    [ObservableProperty] private bool _showQueue;
    /// <summary>Core의 MaxReactionsPerUserPerTrack 과 같은 값이어야 한다.</summary>
    private const int MaxReactionsPerTrack = 3;

    public bool CanReact => HeartsLeft > 0 && CurrentSnapshot?.CurrentTrack is not null;

    public string ReactBlockedReason => CurrentSnapshot?.CurrentTrack is null
        ? "재생 중인 곡이 없습니다"
        : HeartsLeft > 0 ? $"♥ 남은 횟수 {HeartsLeft}회" : "이 곡에는 이미 3번 반응했습니다";

    /// <summary>룸에 들어가기 전에는 배지가 비어 있다. 빈 버튼을 두지 않는다.</summary>
    public string PathBadgeLabel => string.IsNullOrWhiteSpace(PathBadge) ? "시그널 패스" : PathBadge;

    /// <summary>시킹이 막힌 이유. 비어 있으면 툴팁을 띄우지 않는다.</summary>
    public string SeekBlockedReason => SeekingAllowed ? "" : "호스트가 시킹을 잠갔습니다";

    private bool _suppressLinerScrollSend;

    public bool IsLoungePage => SelectedNav?.Id == "lounge";
    public bool IsDevicesPage => SelectedNav?.Id == "devices";
    public bool IsSettingsPage => SelectedNav?.Id == "settings";
    public bool IsHomePage => SelectedNav?.Id == "home";
    public bool IsGenresPage => SelectedNav?.Id == "genres";
    public bool IsComposersPage => SelectedNav?.Id == "composers";
    public bool IsCompositionsPage => SelectedNav?.Id == "compositions";
    public bool IsFoldersPage => SelectedNav?.Id == "folders";
    public bool IsHistoryPage => SelectedNav?.Id == "history";
    public bool IsPlaylistsPage => SelectedNav?.Id == "playlists";
    public bool IsLibraryGrid => IsContentLibrary && !IsHomePage && !IsGenresPage
                                && !IsComposersPage && !IsCompositionsPage && !IsFoldersPage
                                && !IsHistoryPage && !IsPlaylistsPage;
    public bool IsLibraryToolsVisible => SelectedNav?.Id is "home" or "albums" or "artists" or "tracks" or "genres" or "qobuz" or "tidal";
    public bool IsContentLibrary => !IsLoungePage && !IsDevicesPage && !IsSettingsPage;
    public string PlayPauseLabel => IsPlaying ? "⏸" : "▶";
    public string ThemeButtonLabel => DarkTheme ? "라이트" : "다크";
    public string UpdateButtonLabel => UpdateReady ? "업데이트" : "업데이트 확인";
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
    partial void OnSeekingAllowedChanged(bool value) => OnPropertyChanged(nameof(SeekBlockedReason));
    partial void OnPathBadgeChanged(string value) => OnPropertyChanged(nameof(PathBadgeLabel));
    partial void OnHeartsLeftChanged(int value)
    {
        OnPropertyChanged(nameof(CanReact));
        OnPropertyChanged(nameof(ReactBlockedReason));
    }
    partial void OnNowPlayingTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsNpLyrics));
        OnPropertyChanged(nameof(IsNpArtist));
        OnPropertyChanged(nameof(IsNpCredits));
    }
    partial void OnUpdateReadyChanged(bool value) => OnPropertyChanged(nameof(UpdateButtonLabel));
    partial void OnDarkThemeChanged(bool value)
    {
        Prefs.SetBool("dark_theme", value);
        OnPropertyChanged(nameof(ThemeButtonLabel));
        ApplyTheme();
        MonoIcons.ClearCache();
        OnPropertyChanged(nameof(NavItems));
    }
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

    /// <summary>
    /// 검색은 Core 가 한다 — 아티스트 별칭 테이블을 거쳐야 "요네즈 켄시"가 米津玄師를 찾는다.
    /// 타자마다 쏘지 않도록 250ms 묶고, 빈 문자열이면 전체 카탈로그로 돌아간다.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        var q = value?.Trim() ?? "";

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, ct);
                await (q.Length == 0 ? _session.CatalogAsync() : _session.SearchAsync(q));
            }
            catch (OperationCanceledException) { /* 다음 타자가 덮어썼다 */ }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => StatusText = ex.Message);
            }
        }, ct);
    }
    partial void OnSelectedNavChanged(NavItem? value)
    {
        OnPropertyChanged(nameof(IsLoungePage));
        OnPropertyChanged(nameof(IsDevicesPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsHomePage));
        OnPropertyChanged(nameof(IsGenresPage));
        OnPropertyChanged(nameof(IsComposersPage));
        OnPropertyChanged(nameof(IsCompositionsPage));
        OnPropertyChanged(nameof(IsFoldersPage));
        OnPropertyChanged(nameof(IsHistoryPage));
        OnPropertyChanged(nameof(IsPlaylistsPage));
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
            "Composers" => "My Composers",
            "Compositions" => "My Compositions",
            "Folders" => "Folders",
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
        if (value.Id is "folders") _ = Safe(() => _session.FoldersAsync());
        if (value.Id is "playlists") _ = Safe(() => _session.PlaylistsAsync());
        if (value.Id is "devices") _ = Safe(async () => { await _session.EndpointsAsync(); await _session.ListZonesAsync(); });
    }

    public async Task StartAsync()
    {
        // Setup 위저드에서 onboarded=1 을 쓰면 첫 실행 온보딩을 건너뛴다. 가이드로만 재실행.
        ShowOnboarding = !Prefs.GetBool("onboarded");
        DisplayName = Prefs.Get("name", Environment.UserName);
        LibraryPath = Prefs.Get("library_path", LibraryPath);
        ZoneName = Prefs.Get("zone_name", string.IsNullOrWhiteSpace(ZoneName) ? "This PC" : ZoneName);
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

        if (Prefs.GetBool("connect_local_output"))
            await ConnectOutputAsync();

        if (Prefs.GetBool("scan_library_on_start") && !string.IsNullOrWhiteSpace(LibraryPath))
            await ScanLibraryAsync();

        switch (Prefs.Get("streaming_choice", "none"))
        {
            case "tidal":
                await Safe(() => _session.BeginStreamingOAuthAsync(1));
                break;
            case "qobuz":
                await Safe(() => _session.BeginStreamingOAuthAsync(2));
                break;
            case "demo":
                await Safe(() => _session.LinkStreamingAsync(1));
                break;
        }

        // 한 번만 적용
        if (Prefs.Get("streaming_choice", "none") != "none")
            Prefs.Set("streaming_choice", "done");

        _ = CheckForUpdatesAsync();
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

    public void SetIrPathFromPicker(string path) => Audio.SetIrPathFromPicker(path);

    [RelayCommand]
    private void SelectNav(NavItem? item)
    {
        if (item is not null) SelectedNav = item;
    }

    [RelayCommand]
    private void SelectGenre(GenreCount? genre)
    {
        _genreFilter = genre?.Name;
        PageSubtitle = genre is null ? "" : $"{genre.Name} · {genre.TrackCount}곡";
        SelectedNav = NavItems.First(n => n.Id == "tracks");
        ApplyFilter();
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
    private async Task PlayTrackAsync(CatalogTrack? track)
    {
        if (track is null) return;
        await Safe(async () =>
        {
            if (string.IsNullOrWhiteSpace(CurrentRoomId))
                await _session.CreateRoomAsync(Lounge.RoomName, Lounge.RoomMode);
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
    private void ToggleTheme() => DarkTheme = !DarkTheme;

    private void ApplyTheme()
    {
        if (Avalonia.Application.Current is null) return;
        Avalonia.Application.Current.RequestedThemeVariant = DarkTheme
            ? Avalonia.Styling.ThemeVariant.Dark
            : Avalonia.Styling.ThemeVariant.Light;
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

    partial void OnFollowHostViewChanged(bool value)
    {
        if (_suppressLinerScrollSend) return;
        _ = Safe(() => _session.FollowHostAsync(value));
    }

    partial void OnLinerScrollYChanged(double value)
    {
        if (_suppressLinerScrollSend) return;
        // 호스트가 follow를 켠 상태에서 스크롤하면 게스트에게 방송
        if (FollowHostView)
            _ = Safe(() => _session.LinerScrollAsync(value));
    }

    [RelayCommand]
    private async Task ToggleFollowHostAsync()
    {
        FollowHostView = !FollowHostView;
        await Task.CompletedTask;
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
        else if (_genreFilter is not null)
            src = Tracks.Where(t => t.Genres.Contains(_genreFilter, StringComparer.OrdinalIgnoreCase));

        var list = src.Take(500).ToList();
        foreach (var t in list) FilteredTracks.Add(t);

        RebuildHomeRails();
    }


    private void RebuildHomeRails()
    {
        HomeRails.Clear();
        if (Tracks.Count == 0) return;
        HomeRails.Add(new HomeRail("Recently added", Tracks.Take(12)));
        HomeRails.Add(new HomeRail("Albums", Tracks.GroupBy(t => t.AlbumId ?? t.Album).Select(g => g.First()).Take(12)));
        HomeRails.Add(new HomeRail("Hi-Res & DSD", Tracks.Where(t => t.IsDsd || t.SampleRate >= 96000).Take(12)));
        HomeRails.Add(new HomeRail("Streaming", Tracks.Where(t => t.Source is 1 or 2).Take(12)));
    }

    /// <summary>슬라이더를 놓을 때만 보낸다 — 드래그 중 매 픽셀마다 명령을 쏘지 않는다.</summary>
    [RelayCommand]
    private Task ApplyVolumeAsync()
        => SelectedOutput is null
            ? Task.CompletedTask
            : Safe(() => _session.SetVolumeAsync(SelectedOutput.PeerId, Volume));

    /// <summary>출력 선택기를 열 때 존 목록을 새로 받는다 — 존은 룸 스냅샷에 실리지 않는다.</summary>
    [RelayCommand]
    private Task RefreshZonesAsync() => Safe(() => _session.ListZonesAsync());

    [RelayCommand]
    private void SelectOutput(OutputDevice? device)
    {
        if (device is null) return;
        SelectedOutput = device;
        Volume = device.VolumePercent;
    }

    /// <summary>하단 바의 ♥ — 지금 재생 위치에 하트 반응을 남긴다. 토글이 아니다.</summary>
    [RelayCommand]
    private Task HeartAsync() => Safe(() => _session.ReactAsync("❤️"));

    [RelayCommand]
    private void ToggleQueue() => ShowQueue = !ShowQueue;

    [RelayCommand]
    private Task RemoveFromQueueAsync(CatalogTrack? track)
    {
        var i = track is null ? -1 : Lounge.QueueTracks.IndexOf(track);
        return i < 0 ? Task.CompletedTask : Safe(() => _session.RemoveQueueAsync(i));
    }

    [RelayCommand]
    private Task JumpToQueueAsync(CatalogTrack? track)
    {
        var i = track is null ? -1 : Lounge.QueueTracks.IndexOf(track);
        return i < 0 ? Task.CompletedTask : Safe(() => _session.JumpToAsync(i));
    }

    private async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { StatusText = ex.Message; }
    }
}
