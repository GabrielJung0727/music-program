using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Mono.Control.Services;
using Mono.Control.ViewModels;
using Mono.Control.Views;

namespace Mono.Control;

public partial class App : Application
{
    private MainViewModel? _vm;
    private MainWindow? _main;
    private TrayIcon? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // UI 스레드 예외는 여기로 온다. Avalonia 가 초기화된 뒤에 걸어야 한다 —
        // Main 에서 Dispatcher.UIThread 를 먼저 건드리면 플랫폼 없는 디스패처가 만들어져
        // MainLoop 가 PlatformNotSupportedException 으로 죽는다.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Program.LogDiagnostic("ui", e.Exception);
            // 기록했으니 버틴다 — 한 화면의 오류로 앱 전체가 사라지면 안 된다.
            e.Handled = true;
        };
        Dispatcher.UIThread.ShutdownStarted += (_, _) => Program.LogDiagnostic("shutdown", "창이 닫혔습니다");

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

            _tray = new TrayIcon
            {
                ToolTipText = "mono",
                IsVisible = true,
                Icon = LoadTrayIcon(_vm.DarkTheme),
                Menu = new NativeMenu
                {
                    new NativeMenuItem("열기") { Command = new RelayAction(() => Dispatcher.UIThread.Post(ShowMain)) },
                    new NativeMenuItem("종료") { Command = new RelayAction(() => Dispatcher.UIThread.Post(QuitFromTray)) }
                }
            };
            TrayIcon.SetIcons(this, [_tray]);
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.DarkTheme) && _tray is not null)
                    _tray.Icon = LoadTrayIcon(_vm.DarkTheme);
            };

            _ = _vm.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static WindowIcon LoadTrayIcon(bool dark)
    {
        var uri = dark
            ? "avares://Mono.Control/Assets/icons/dark/tray/mono-tray-dark.png"
            : "avares://Mono.Control/Assets/icons/tray/mono-tray-light.png";
        return new WindowIcon(AssetLoader.Open(new Uri(uri)));
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
