using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Mono.Control.ViewModels;

namespace Mono.Control.Views;

public partial class MainWindow : Window
{
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
}
