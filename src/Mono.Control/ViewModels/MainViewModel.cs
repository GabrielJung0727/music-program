using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _clockTimer.Tick += (_, _) => TickClock();
        _clockTimer.Start();
    }

    public ObservableCollection<NavItem> NavItems { get; }
    public ObservableCollection<CatalogTrack> Tracks { get; } = new();
    public ObservableCollection<CatalogTrack> FilteredTracks { get; } = new();
    public ObservableCollection<RoomListItem> Rooms { get; } = new();
    public ObservableCollection<string> ChatLines { get; } = new();
    public ObservableCollection<CatalogTrack> QueueTracks { get; } = new();
    public ObservableCollection<CatalogTrack> AutoplayChoices { get; } = new();

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
    [ObservableProperty] private string _roomChip = "룸 없음";
    [ObservableProperty] private string _syncText = "sync —";
    [ObservableProperty] private string _pathBadge = "";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _seekValue;
    [ObservableProperty] private double _seekMaximum = 1;
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private string _remainText = "0:00";
    [ObservableProperty] private bool _showAutoplay;
    [ObservableProperty] private string _currentRoomId = "";
    [ObservableProperty] private string _wikiText = "";
    [ObservableProperty] private bool _darkTheme;
    [ObservableProperty] private CatalogTrack? _selectedTrack;

    public bool IsLoungePage => SelectedNav?.Id == "lounge";
    public bool IsDevicesPage => SelectedNav?.Id == "devices";
    public bool IsSettingsPage => SelectedNav?.Id == "settings";
    public bool IsLibraryToolsVisible => SelectedNav?.Id is "home" or "albums" or "artists" or "tracks" or "genres" or "qobuz" or "tidal";
    public bool IsContentLibrary => !IsLoungePage && !IsDevicesPage && !IsSettingsPage;
    public string PlayPauseLabel => IsPlaying ? "⏸" : "▶";
    public bool IsObStep0 => OnboardingStep == 0;
    public bool IsObStep1 => OnboardingStep == 1;
    public bool IsObStep2 => OnboardingStep == 2;
    public bool IsObStep3 => OnboardingStep == 3;
    public bool IsObStep4 => OnboardingStep == 4;
    public bool IsObStep5 => OnboardingStep == 5;

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayPauseLabel));
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
            "라운지" => "라운지",
            "Audio" => "Audio devices",
            "Settings" => "Settings",
            _ => value.Label
        };
        PageSubtitle = value.Section;
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
        // Core/Output는 트레이 유지 정책: 기본은 Output만 정리, Core는 유지하지 않고 함께 종료(단일 PC UX)
        _supervisor.StopAll();
    }

    [RelayCommand]
    private void SelectNav(NavItem? item)
    {
        if (item is not null) SelectedNav = item;
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
    private async Task LinkTidalAsync() => await Safe(() => _session.LinkStreamingAsync(1));

    [RelayCommand]
    private async Task LinkQobuzAsync() => await Safe(() => _session.LinkStreamingAsync(2));

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
    private void ToggleTheme()
    {
        DarkTheme = !DarkTheme;
        if (Avalonia.Application.Current is not null)
            Avalonia.Application.Current.RequestedThemeVariant =
                DarkTheme ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
    }

    [RelayCommand]
    private async Task SeekToAsync()
    {
        await Safe(() => _session.SeekAsync((long)SeekValue));
    }

    private void OnMessage(MonoMessage msg)
    {
        Dispatcher.UIThread.Post(() => HandleMessage(msg));
    }

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
                break;
            case MessageTypes.Chat:
                if (!string.IsNullOrWhiteSpace(msg.Text))
                    ChatLines.Add($"{msg.DisplayName ?? msg.PeerId}: {msg.Text}");
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
        catch
        {
            /* ignore malformed */
        }
        ApplyFilter();
        PageSubtitle = $"{Tracks.Count} tracks";
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

            var media = node["mediaTimeMs"]?.GetValue<long>() ?? 0;
            var duration = Math.Max(1, node["durationMs"]?.GetValue<long>() ?? 1);
            SeekMaximum = duration;
            _baseMedia = media;
            _baseLocal = Environment.TickCount64;
            _playingClock = IsPlaying;
            SeekValue = media;
            UpdateTimeTexts(media, duration);

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
            }

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

            // members sync stats
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
                        Artist = c?["artist"]?.GetValue<string>()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = "스냅샷 파싱: " + ex.Message;
        }
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
        else if (nav is "qobuz" or "tidal" or "genres" or "home")
            src = Tracks; // same catalog; streaming filter later

        var q = SearchText.Trim();
        if (q.Length > 0)
        {
            src = src.Where(t =>
                (t.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Artist?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Album?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var t in src.Take(500)) FilteredTracks.Add(t);
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
