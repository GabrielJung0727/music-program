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
