using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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

    private void SeekLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            _ = vm.SeekToCommand.ExecuteAsync(null);
    }

    private async void PickLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "음악 라이브러리 폴더",
            AllowMultiple = false
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            vm.SetLibraryPathFromPicker(path);
    }

    private async void PickIrClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "룸 IR (WAV / ZIP)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Impulse response") { Patterns = ["*.wav", "*.zip"] },
                FilePickerFileTypes.All
            ]
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            vm.SetIrPathFromPicker(path);
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
