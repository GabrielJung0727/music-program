using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mono.Control.Models;
using Mono.Control.Services;

namespace Mono.Control.ViewModels.Pages;

/// <summary>
/// Genres · Composers · Compositions · Folders — 같은 카탈로그를 다른 축으로 본다.
/// 집계는 전부 여기서 하고, 작품 그룹핑 기준(workKey)은 Core 가 정한 것을 그대로 쓴다.
/// </summary>
public sealed partial class LibraryViewModel : PageViewModel
{
    public LibraryViewModel(CoreSession session) : base(session) { }

    public ObservableCollection<GenreCount> Genres { get; } = new();
    public ObservableCollection<ComposerEntry> Composers { get; } = new();
    public ObservableCollection<CompositionEntry> Compositions { get; } = new();
    public ObservableCollection<FolderEntry> Folders { get; } = new();
    public ObservableCollection<HistoryEntry> History { get; } = new();
    public ObservableCollection<PlaylistEntry> Playlists { get; } = new();

    [ObservableProperty] private string _emptyGenresHint = "";
    [ObservableProperty] private AlbumDetail? _selectedAlbum;

    /// <summary>안내 문구 표시 여부. XAML 에서 컨버터를 쓰지 않도록 bool 로 낸다.</summary>
    public bool HasEmptyGenresHint => !string.IsNullOrEmpty(EmptyGenresHint);

    partial void OnEmptyGenresHintChanged(string value) => OnPropertyChanged(nameof(HasEmptyGenresHint));

    /// <summary>오버레이 표시 여부. XAML 에서 컨버터를 쓰지 않도록 bool 로 낸다.</summary>
    public bool HasSelectedAlbum => SelectedAlbum is not null;

    partial void OnSelectedAlbumChanged(AlbumDetail? value) => OnPropertyChanged(nameof(HasSelectedAlbum));

    public void ApplyAlbum(AlbumDetail? album) => SelectedAlbum = album;

    public void Rebuild(IEnumerable<CatalogTrack> tracks)
    {
        var list = tracks.ToList();

        Genres.Clear();
        foreach (var g in list
                     .SelectMany(t => t.Genres)
                     .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key))
        {
            Genres.Add(new GenreCount(g.Key, g.Count()));
        }

        // 마이그레이션은 기존 행을 보존하므로, 재스캔 전에는 장르가 비어 있을 수 있다.
        EmptyGenresHint = Genres.Count == 0 && list.Count > 0
            ? "장르 태그가 아직 없습니다 — Settings 에서 「라이브러리 스캔」을 한 번 돌리세요."
            : "";

        Composers.Clear();
        foreach (var c in list
                     .SelectMany(t => t.Composers)
                     .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(c => c.Count())
                     .ThenBy(c => c.Key))
        {
            Composers.Add(new ComposerEntry(c.Key, c.Count()));
        }

        Compositions.Clear();
        foreach (var w in list
                     .Where(t => !string.IsNullOrEmpty(t.WorkKey))
                     .GroupBy(t => t.WorkKey!)
                     .OrderBy(w => w.First().WorkTitle))
        {
            var head = w.First();
            Compositions.Add(new CompositionEntry(
                w.Key,
                head.WorkTitle ?? head.Title,
                head.Composers.FirstOrDefault() ?? "",
                w.Count()));
        }
    }

    public void ApplyFolders(IEnumerable<FolderEntry> folders)
    {
        Folders.Clear();
        foreach (var f in folders) Folders.Add(f);
    }

    public void ApplyHistory(IEnumerable<HistoryEntry> entries)
    {
        History.Clear();
        foreach (var e in entries.OrderByDescending(e => e.HeardAt)) History.Add(e);
    }

    public void ApplyPlaylists(IEnumerable<PlaylistEntry> lists)
    {
        Playlists.Clear();
        foreach (var p in lists) Playlists.Add(p);
    }

    /// <summary>히스토리에서 다시 듣기 — 큐에 다시 싣는다.</summary>
    [RelayCommand]
    private Task ReplayAsync(HistoryEntry? entry)
        => entry is null ? Task.CompletedTask : Safe(() => Session.EnqueueAsync(entry.TrackId));

    /// <summary>플레이리스트를 큐에 싣는다. 큐를 비우지 않고 뒤에 붙인다.</summary>
    [RelayCommand]
    private Task LoadPlaylistAsync(PlaylistEntry? playlist)
        => playlist is null ? Task.CompletedTask : Safe(() => Session.LoadPlaylistAsync(playlist.Id));

    [RelayCommand]
    private Task ExportM3uAsync(PlaylistEntry? playlist)
        => playlist is null ? Task.CompletedTask : Safe(() => Session.ExportM3uAsync(playlist.Id));

    [RelayCommand]
    private Task OpenAlbumAsync(CatalogTrack? track)
        => string.IsNullOrEmpty(track?.AlbumId)
            ? Task.CompletedTask
            : Safe(() => Session.AlbumAsync(track.AlbumId!));

    [RelayCommand]
    private void CloseAlbum() => SelectedAlbum = null;
}
