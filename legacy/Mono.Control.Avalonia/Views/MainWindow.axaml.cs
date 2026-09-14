using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mono.Control.Services;
using Mono.Control.ViewModels;
using Mono.Control.Views.Pages;

namespace Mono.Control.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;
    private ContentControl? _pageHost;
    private MainViewModel? _vm;
    private readonly Dictionary<string, Avalonia.Controls.Control> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WirePageHost();
        DataContextChanged += (_, _) => WirePageHost();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void WirePageHost()
    {
        _pageHost ??= this.FindControl<ContentControl>("PageHost");
        if (DataContext is not MainViewModel vm || _pageHost is null) return;
        if (!ReferenceEquals(_vm, vm))
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        ShowPage(_vm.SelectedNav?.Id ?? "home");
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedNav) or nameof(MainViewModel.IsLibraryGrid))
            ShowPage(_vm?.SelectedNav?.Id ?? "home");
    }

    /// <summary>
    /// 한 번에 한 페이지만 트리에 올린다. IsVisible 로 숨긴 채 HomeRails 를 건드리면
    /// Avalonia/Skia 가 네이티브로 죽는 경우가 있다 (Settings→Home).
    /// </summary>
    private void ShowPage(string id)
    {
        if (_pageHost is null || _vm is null) return;

        var pageId = id switch
        {
            "genres" or "composers" or "compositions" or "folders" or "history" or "playlists"
                or "lounge" or "devices" or "settings" or "home" => id,
            _ when _vm.IsLibraryGrid => "library",
            _ => "home"
        };

        if (!_pages.TryGetValue(pageId, out var page))
        {
            page = CreatePage(pageId);
            _pages[pageId] = page;
        }

        ApplyPageContext(pageId, page);
        if (!ReferenceEquals(_pageHost.Content, page))
            _pageHost.Content = page;
    }

    private static Avalonia.Controls.Control CreatePage(string pageId) => pageId switch
    {
        "home" => new HomePage(),
        "genres" => new GenresPage(),
        "composers" => new ComposersPage(),
        "compositions" => new CompositionsPage(),
        "folders" => new FoldersPage(),
        "history" => new HistoryPage(),
        "playlists" => new PlaylistsPage(),
        "library" => new LibraryPage(),
        "lounge" => new LoungePage(),
        "devices" => new AudioPage(),
        "settings" => new SettingsPage(),
        _ => new HomePage()
    };

    private void ApplyPageContext(string pageId, Avalonia.Controls.Control page)
    {
        if (_vm is null) return;
        page.DataContext = pageId switch
        {
            "genres" or "composers" or "compositions" or "folders" or "history" or "playlists" => _vm.Library,
            "lounge" => _vm.Lounge,
            "devices" => _vm.Audio,
            _ => _vm
        };
    }

    private void OnSeeked(object? sender, double mediaTimeMs)
    {
        if (DataContext is MainViewModel vm)
            _ = vm.SeekToCommand.ExecuteAsync(null);
    }

    private void VolumeReleased(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            _ = vm.ApplyVolumeCommand.ExecuteAsync(null);
    }

    private async void PickLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (await FilePickers.PickLibraryFolderAsync(this) is { } path)
            vm.SetLibraryPathFromPicker(path);
    }

    /// <summary>
    /// 문서 §6 단축키. 글자를 입력 중일 때는 Space·/ 를 가로채지 않는다 —
    /// 채팅이나 검색을 치다가 재생이 멈추면 안 된다.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var typing = FocusManager?.GetFocusedElement() is TextBox;

        switch (e.Key)
        {
            case Key.Space when !typing:
                _ = vm.PlayPauseCommand.ExecuteAsync(null);
                e.Handled = true;
                break;

            case Key.Escape:
                // 위에 덮인 것부터 닫는다. 온보딩은 완료해야 닫힌다.
                if (vm.ShowOnboarding) return;
                if (vm.Library.SelectedAlbum is not null) vm.Library.CloseAlbumCommand.Execute(null);
                else if (vm.ShowNowPlaying) vm.CloseNowPlayingCommand.Execute(null);
                else if (vm.ShowQueue) vm.ToggleQueueCommand.Execute(null);
                else return;
                e.Handled = true;
                break;

            case Key.OemQuestion when !typing:
            case Key.Divide when !typing:
                SearchBox.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose || DataContext is not MainViewModel vm) return;
        if (!vm.CloseToTray) return;
        e.Cancel = true;
        Hide();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }
}
