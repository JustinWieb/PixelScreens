using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Pixelscreens.ViewModels;
using Pixelscreens.Views;

namespace Pixelscreens;

public partial class App : Application
{
    private MainViewModel? _vm;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs();
            var vm = _vm = new MainViewModel();
            var window = _window = new MainWindow { DataContext = vm };

            if (args.Contains("--tab=audio")) vm.SelectedTab = 1;
            if (args.Contains("--tab=layout")) vm.SelectedTab = 2;
            if (args.Contains("--tab=settings")) vm.SelectedTab = 3;

            var startHidden = (args.Contains("--minimized") || vm.Settings.StartMinimized) && !args.Any(a => a.StartsWith("--tab="));

            vm.ShowRequested += ShowWindow;

            window.Closing += (_, e) =>
            {
                if (!_exiting && vm.Settings.CloseToTray)
                {
                    e.Cancel = true;
                    window.Hide();
                    return;
                }
                vm.Shutdown();
                if (!_exiting) { _exiting = true; desktop.Shutdown(); }
            };

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => { _tray?.Dispose(); vm.Dispose(); };

            SetupTray(vm);
            _ = InitialiseAsync(vm, args);
            if (!startHidden) window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitialiseAsync(MainViewModel vm, string[] args)
    {
        await vm.InitialiseAsync();
        if (args.Contains("--start-hook") && !vm.Layout.IsRunning) await vm.Layout.StartCommand.ExecuteAsync(null);
        RebuildTrayMenu(vm);
        vm.Profiles.Profiles.CollectionChanged += (_, _) => RebuildTrayMenu(vm);
        vm.Audio.Profiles.CollectionChanged += (_, _) => RebuildTrayMenu(vm);
        vm.Layout.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LayoutViewModel.IsRunning)) RebuildTrayMenu(vm); };
    }

    private void SetupTray(MainViewModel vm)
    {
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Pixelscreens/Assets/pixelscreens.ico"))),
            ToolTipText = "PixelScreens",
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => ShowWindow();
        RebuildTrayMenu(vm);
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    private void RebuildTrayMenu(MainViewModel vm)
    {
        if (_tray is null) return;
        var menu = new NativeMenu();
        menu.Add(Item("Open PixelScreens", ShowWindow));
        menu.Add(new NativeMenuItemSeparator());

        var profiles = new NativeMenu();
        foreach (var p in vm.Profiles.Profiles)
        {
            var row = p;
            profiles.Add(Item((row.IsActive ? "● " : "   ") + row.Name, async () => await vm.Profiles.ApplyByUuidAsync(row.Uuid)));
        }
        if (vm.Profiles.Profiles.Count == 0) profiles.Add(new NativeMenuItem("(none saved)") { IsEnabled = false });
        menu.Add(new NativeMenuItem("Display profiles") { Menu = profiles });

        var audio = new NativeMenu();
        foreach (var a in vm.Audio.Profiles)
        {
            var row = a;
            audio.Add(Item((row.IsActive ? "● " : "   ") + row.Name, async () => await vm.Audio.ApplyByUuidAsync(row.Uuid)));
        }
        if (vm.Audio.Profiles.Count == 0) audio.Add(new NativeMenuItem("(none saved)") { IsEnabled = false });
        menu.Add(new NativeMenuItem("Audio profiles") { Menu = audio });

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(vm.Layout.IsRunning ? "Disable cursor engine" : "Enable cursor engine", async () => await vm.ToggleCursorAsync()));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Exit", Exit));
        _tray.Menu = menu;
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        _vm?.Shutdown();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }
}
