using Avalonia.Controls;
using Avalonia.Interactivity;
using Mono.Control.Services;
using Mono.Control.ViewModels;

namespace Mono.Control.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    private async void PickLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (await FilePickers.PickLibraryFolderAsync(this) is { } path)
            vm.SetLibraryPathFromPicker(path);
    }
}
