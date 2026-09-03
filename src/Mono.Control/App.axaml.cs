using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Mono.Control.Services;
using Mono.Control.ViewModels;
using Mono.Control.Views;

namespace Mono.Control;

public partial class App : Application
{
    private MainViewModel? _vm;
    private MainWindow? _main;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = new CoreSession();
            var supervisor = new ProcessSupervisor();
            _vm = new MainViewModel(session, supervisor);
            _main = new MainWindow { DataContext = _vm };
            desktop.MainWindow = _main;

            desktop.ShutdownRequested += async (_, e) =>
            {
                e.Cancel = true;
                await _vm.ShutdownAsync();
                desktop.Shutdown();
            };

            TrayIcon.SetIcons(this, new TrayIcons
            {
                new TrayIcon
                {
                    ToolTipText = "mono",
                    IsVisible = true,
                    Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(
                        new Uri("avares://Mono.Control/Assets/icons/tray/mono-tray-light.png"))),
                    Menu = new NativeMenu
                    {
                        new NativeMenuItem("열기") { Command = new RelayAction(() => Dispatcher.UIThread.Post(ShowMain)) },
                        new NativeMenuItem("종료") { Command = new RelayAction(() => Dispatcher.UIThread.Post(QuitFromTray)) }
                    }
                }
            });

            _ = _vm.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        _main.Activate();
        _main.WindowState = WindowState.Normal;
    }

    private async void QuitFromTray()
    {
        if (_vm is not null) await _vm.ShutdownAsync();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _main?.ForceClose();
            desktop.Shutdown();
        }
    }

    private sealed class RelayAction(Action action) : System.Windows.Input.ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    }
}
