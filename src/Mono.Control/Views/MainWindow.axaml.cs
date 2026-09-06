using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mono.Control.Services;
using Mono.Control.ViewModels;

namespace Mono.Control.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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
