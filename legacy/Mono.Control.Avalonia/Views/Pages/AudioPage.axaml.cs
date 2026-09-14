using Avalonia.Controls;
using Avalonia.Interactivity;
using Mono.Control.Services;
using Mono.Control.ViewModels;

namespace Mono.Control.Views.Pages;

public partial class AudioPage : UserControl
{
    public AudioPage() => InitializeComponent();

    private async void PickIrClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (await FilePickers.PickImpulseResponseAsync(this) is { } path)
            vm.SetIrPathFromPicker(path);
    }
}
