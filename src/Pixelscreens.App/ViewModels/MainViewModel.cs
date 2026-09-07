using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Pixelscreens.Services;

namespace Pixelscreens.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly CursorEngineService _cursorEngine;
    private readonly HotkeyService _hotkeys = new();

    public AppSettings Settings { get; }

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        _cursorEngine = new CursorEngineService(Settings);

        Profiles = new ProfilesViewModel(new DisplayProfileService(), Settings, RebindHotkeys);
        Audio = new AudioViewModel(new AudioProfileService(), Settings, RebindHotkeys);
        Layout = new LayoutViewModel(_cursorEngine);
        Options = new SettingsViewModel(Settings, () => _cursorEngine.ApplyOptionsAsync(Settings), RebindHotkeys);

        Profiles.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(ProfilesViewModel.Status) or nameof(ProfilesViewModel.EngineOk)) RaiseFooter(); };
        Audio.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(AudioViewModel.Status) or nameof(AudioViewModel.EngineOk)) RaiseFooter(); };
        Layout.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(LayoutViewModel.Status) or nameof(LayoutViewModel.EngineOk)) RaiseFooter(); };
        Options.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(SettingsViewModel.Status)) RaiseFooter(); };

        _hotkeys.Triggered += action => Dispatcher.UIThread.Post(async () => await OnHotkeyAsync(action));
    }

    public ProfilesViewModel Profiles { get; }
    public AudioViewModel Audio { get; }
    public LayoutViewModel Layout { get; }
    public SettingsViewModel Options { get; }

    /// <summary>Raised when the app wants its window shown (tray Open, hotkey).</summary>
    public event Action? ShowRequested;

    [ObservableProperty] private int _selectedTab;

    public bool ShowProfiles => SelectedTab == 0;
    public bool ShowAudio => SelectedTab == 1;
    public bool ShowLayout => SelectedTab == 2;
    public bool ShowSettings => SelectedTab == 3;

    public string FooterStatus => SelectedTab switch
    {
        1 => Audio.Status,
        2 => Layout.Status,
        3 => Options.Status.Length > 0 ? Options.Status : Layout.Status,
        _ => Profiles.Status,
    };

    public bool FooterOk => SelectedTab switch
    {
        1 => Audio.EngineOk,
        2 => Layout.EngineOk,
        3 => true,
        _ => Profiles.EngineOk,
    };

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(ShowProfiles));
        OnPropertyChanged(nameof(ShowAudio));
        OnPropertyChanged(nameof(ShowLayout));
        OnPropertyChanged(nameof(ShowSettings));
        RaiseFooter();
    }

    private void RaiseFooter()
    {
        OnPropertyChanged(nameof(FooterStatus));
        OnPropertyChanged(nameof(FooterOk));
    }

    public async Task InitialiseAsync()
    {
        await Profiles.RefreshAsync();
        await Audio.RefreshAsync();
        await Layout.RefreshAsync();
        _hotkeys.Start();
        RebindHotkeys();
        if (Settings.EnableCursorOnStart && Layout.EngineOk)
            await Layout.StartCommand.ExecuteAsync(null);
    }

    private void RebindHotkeys() => _hotkeys.Rebind(Settings.Hotkeys);

    private async Task OnHotkeyAsync(string action)
    {
        if (action == "show") ShowRequested?.Invoke();
        else if (action == "cursor.toggle") await ToggleCursorAsync();
        else if (action.StartsWith("profile:")) await Profiles.ApplyByUuidAsync(action[8..]);
        else if (action.StartsWith("audio:")) await Audio.ApplyByUuidAsync(action[6..]);
    }

    public Task ToggleCursorAsync() =>
        Layout.IsRunning ? Layout.StopCommand.ExecuteAsync(null) : Layout.StartCommand.ExecuteAsync(null);

    public void Shutdown()
    {
        try { _cursorEngine.Shutdown(); } catch { /* best effort on exit */ }
    }

    public void Dispose()
    {
        _hotkeys.Dispose();
        _cursorEngine.Dispose();
    }
}
