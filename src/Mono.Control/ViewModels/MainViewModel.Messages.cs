using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Mono.Control.Models;
using Mono.Control.Services;
using Mono.Protocol;
using Mono.Shared;

namespace Mono.Control.ViewModels;

public partial class MainViewModel
{
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
                LoadCatalog(msg.Body, isFullCatalog: true);
                break;
            case MessageTypes.Search:
                LoadCatalog(msg.Body, isFullCatalog: false);
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
                    Lounge.ChatLines.Add($"{msg.DisplayName ?? msg.PeerId}: {msg.Text}");
                break;
            case MessageTypes.LinkStreaming:
                StatusText = (msg.Ok ?? false) ? "스트리밍 연동 응답" : (msg.Error ?? "스트리밍");
                if (!string.IsNullOrWhiteSpace(msg.Body) && msg.Body.Contains("\"demo\":true", StringComparison.OrdinalIgnoreCase))
                    StatusText = "파트너 키 없음 — 데모 토큰으로 연동했습니다.";
                else if (!string.IsNullOrWhiteSpace(msg.Body) && msg.Body.Contains("authUrl", StringComparison.OrdinalIgnoreCase))
                    StatusText = "OAuth 브라우저 열림 — 데모면 ‘데모 토큰’으로 완료";
                break;
            case MessageTypes.History:
                LoadHistory(msg.Body);
                break;
            case MessageTypes.Playlists:
                LoadPlaylists(msg.Body);
                break;
            case MessageTypes.Album:
                LoadAlbum(msg.Body);
                break;
            case MessageTypes.Folders:
                LoadFolders(msg.Body);
                break;
            case MessageTypes.ListZones:
                LoadZones(msg.Body);
                break;
            case MessageTypes.SyncProbe:
                Audio.SyncProbeText = msg.Body ?? "";
                StatusText = "Sync probe 수신";
                break;
        }
    }

    /// <summary>
    /// catalog 와 search 는 같은 모양을 돌려주지만 뜻이 다르다. 검색 결과는 라이브러리의
    /// 일부이므로, 장르·작곡가·작품 집계는 전체 카탈로그일 때만 다시 만든다 —
    /// 그러지 않으면 검색 한 번에 Genres 화면이 결과만큼으로 줄어든다.
    /// </summary>
    private void LoadCatalog(string? body, bool isFullCatalog)
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
        if (isFullCatalog) Library.Rebuild(Tracks);
        PageSubtitle = isFullCatalog ? $"{Tracks.Count} tracks" : $"검색 결과 {Tracks.Count}곡";
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
            var bmp = await ArtCache.GetAsync(t.AbsoluteArtUrl, ct, decodeWidth: 160);
            if (bmp is not null)
                await Dispatcher.UIThread.InvokeAsync(() => t.Cover = bmp);
        }
        Audio.ArtPerfText = ArtCache.StatsText();
    }

    private void LoadAlbum(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Library.ApplyAlbum(null); return; }
        try { Library.ApplyAlbum(JsonSerializer.Deserialize<AlbumDetail>(body, Json)); }
        catch { /* 형식이 어긋나면 열지 않는다 */ }
    }

    private void LoadHistory(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Library.ApplyHistory([]); return; }
        try { Library.ApplyHistory(JsonSerializer.Deserialize<List<HistoryEntry>>(body, Json) ?? []); }
        catch { /* 형식이 어긋나면 이전 목록을 유지한다 */ }
    }

    private void LoadPlaylists(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Library.ApplyPlaylists([]); return; }
        try { Library.ApplyPlaylists(JsonSerializer.Deserialize<List<PlaylistEntry>>(body, Json) ?? []); }
        catch { /* 형식이 어긋나면 이전 목록을 유지한다 */ }
    }

    private void LoadFolders(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Library.ApplyFolders([]); return; }
        try
        {
            Library.ApplyFolders(JsonSerializer.Deserialize<List<FolderEntry>>(body, Json) ?? []);
        }
        catch { /* 형식이 어긋나면 이전 목록을 유지한다 */ }
    }

    private void LoadZones(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Zones.Clear(); return; }
        try
        {
            var list = JsonSerializer.Deserialize<List<ZoneItem>>(body, Json) ?? [];
            Zones.Clear();
            foreach (var z in list) Zones.Add(z);
        }
        catch { /* 형식이 어긋나면 이전 목록을 유지한다 */ }
    }

    private void LoadRooms(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { Lounge.ApplyRoomList([]); return; }
        try
        {
            Lounge.ApplyRoomList(JsonSerializer.Deserialize<List<RoomListItem>>(body, Json) ?? []);
        }
        catch { /* 형식이 어긋나면 이전 목록을 유지한다 */ }
    }

    private void ApplyRoomState(string? body)
    {
        var snap = RoomSnapshot.Parse(body);
        if (snap is null)
        {
            // 빈 본문은 정상(하트비트). 내용이 있는데 못 읽으면 계약이 어긋난 것이니 드러낸다.
            if (!string.IsNullOrWhiteSpace(body)) StatusText = "스냅샷 파싱 실패";
            return;
        }
        CurrentSnapshot = snap;

        CurrentRoomId = snap.Id;
        RoomChip = $"{(string.IsNullOrEmpty(snap.Name) ? "룸" : snap.Name)} · {snap.Id[..Math.Min(6, snap.Id.Length)]}";
        IsPlaying = snap.Playing;
        PathBadge = snap.PathBadge ?? (snap.BitPerfect ? "Bit-perfect" : "Processed");
        NowBadge = PathBadge;

        SignalPathText = snap.DspEnabled
            ? $"Decode → DSP({snap.DspPreset}"
              + (string.IsNullOrWhiteSpace(snap.DeviceEqProfile) ? "" : "/" + snap.DeviceEqProfile)
              + (string.IsNullOrWhiteSpace(snap.ConvolutionIrPath) ? "" : "+IR")
              + $") → Output · {PathBadge}"
            : $"Decode → Bit-perfect → Output · {PathBadge}";
        var duration = Math.Max(1, snap.DurationMs);
        SeekMaximum = duration;
        _baseMedia = snap.MediaTimeMs;
        _baseLocal = Environment.TickCount64;
        _playingClock = IsPlaying;
        SeekValue = snap.MediaTimeMs;
        UpdateTimeTexts(snap.MediaTimeMs, duration);

        LinerNotes = snap.LinerNotes ?? "";
        CreditsText = snap.Credits ?? "";

        _suppressLinerScrollSend = true;
        FollowHostView = snap.FollowHostView;
        LinerScrollY = snap.LinerScrollY;
        _suppressLinerScrollSend = false;

        CurrentLyric = snap.CurrentLyric ?? "";
        LyricLines.Clear();
        foreach (var line in snap.Lyrics)
            if (!string.IsNullOrWhiteSpace(line.Text)) LyricLines.Add(line.Text);

        if (snap.CurrentTrack is { } ct)
        {
            NowTitle = string.IsNullOrWhiteSpace(ct.Title) ? "트랙" : ct.Title;
            NowArtist = string.Join(" · ", new[] { ct.ArtistName, ct.AlbumTitle }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            NowArtUrl = string.IsNullOrWhiteSpace(ct.ArtUrl) ? "" : "http://127.0.0.1:7702" + ct.ArtUrl;
            _ = RefreshNowArtAsync(NowArtUrl);
            if (!string.IsNullOrWhiteSpace(ct.ArtistId) && string.IsNullOrWhiteSpace(ArtistBio))
                _ = Safe(() => _session.WikiAsync(ct.ArtistId!));
        }

        ArtistBio = snap.Artist?.Bio ?? ArtistBio;

        // 기존 동작 유지: 통계가 있는 첫 멤버를 쓰고, 없으면 직전 값을 그대로 둔다.
        foreach (var m in snap.Members)
        {
            if (m.Stats is not { } st) continue;
            SyncText = $"sync {st.OffsetMs:+0.00;-0.00}ms · jitter {st.JitterMs:0.00}ms";
            break;
        }

        AutoplayChoices.Clear();
        if (snap.Autoplay is { } ap)
        {
            foreach (var c in ap.Candidates)
            {
                AutoplayChoices.Add(new CatalogTrack
                {
                    Id = c.Id,
                    Title = c.Title,
                    Artist = c.ArtistName,
                    DurationMs = c.DurationMs,
                    Badge = c.Badge,
                    ArtUrl = c.ArtUrl
                });
            }
        }
        // 기존 코드는 candidates 배열이 비어 있어도 카드를 띄웠다. 빈 카드는 띄우지 않는다.
        ShowAutoplay = AutoplayChoices.Count > 0;

        Heatmap = snap.Heatmap;
        Pins = snap.Pins;
        SeekingAllowed = snap.SeekingAllowed;

        HeartCount = snap.ReactionCounts.GetValueOrDefault("❤️");
        var mine = snap.CurrentTrack is null
            ? 0
            : snap.Reactions.Count(r => r.PeerId == _session.PeerId && r.TrackId == snap.CurrentTrack.Id);
        HeartsLeft = Math.Max(0, MaxReactionsPerTrack - mine);
        OnPropertyChanged(nameof(CanReact));
        OnPropertyChanged(nameof(ReactBlockedReason));

        var keepOutput = SelectedOutput?.PeerId;
        Outputs.Clear();
        foreach (var o in snap.Outputs) Outputs.Add(OutputDevice.From(o));
        SelectedOutput = Outputs.FirstOrDefault(o => o.PeerId == keepOutput) ?? Outputs.FirstOrDefault();

        if (SelectedOutput is { } sel)
        {
            Volume = sel.VolumePercent;
            OutputChip = $"{sel.Name} · {sel.Badge}";
            // Core의 QualityPolicyEngine.AllowsDigitalVolume 과 같은 판정.
            VolumeEnabled = sel.HardwareVolume
                            || (snap.Mode != RoomMode.Audiophile
                                && snap.QualityPolicy != QualityPolicy.RequireBitPerfect);
            VolumeBlockedReason = VolumeEnabled
                ? ""
                : "이 룸은 디지털 볼륨 감쇠를 금지합니다 — 하드웨어 볼륨을 쓰세요";
        }
        else
        {
            OutputChip = "출력 없음";
            VolumeEnabled = false;
            VolumeBlockedReason = "출력 장치가 없습니다 — 「출력 연결」을 누르세요";
        }

        Lounge.ApplySnapshot(snap);
        Audio.ApplySnapshot(snap);
    }

}
