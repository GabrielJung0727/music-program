using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Mono.Control.Services;
using Mono.Control.ViewModels;
using Mono.Control.Views;

namespace Mono.Control;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = new CoreSession();
            var supervisor = new ProcessSupervisor();
            var vm = new MainViewModel(session, supervisor);
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownRequested += async (_, e) =>
            {
                e.Cancel = true;
                await vm.ShutdownAsync();
                desktop.Shutdown();
            };
            _ = vm.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
