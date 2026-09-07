using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pixelscreens.Services;

namespace Pixelscreens.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _s;
    private readonly Func<Task> _cursorOptionsChanged;
    private readonly Action _hotkeysChanged;
    private bool _loading;

    public SettingsViewModel(AppSettings settings, Func<Task> cursorOptionsChanged, Action hotkeysChanged)
    {
        _s = settings;
        _cursorOptionsChanged = cursorOptionsChanged;
        _hotkeysChanged = hotkeysChanged;
        _loading = true;
        StartMinimized = _s.StartMinimized;
        CloseToTray = _s.CloseToTray;
        EnableCursorOnStart = _s.EnableCursorOnStart;
        Algorithm = _s.Algorithm;
        LoopX = _s.LoopX;
        LoopY = _s.LoopY;
        MaxTravelDistance = _s.MaxTravelDistance;
        AllowDiscontinuity = _s.AllowDiscontinuity;
        AllowOverlaps = _s.AllowOverlaps;
        AdjustPointer = _s.AdjustPointer;
        AdjustSpeed = _s.AdjustSpeed;
        FreelookEnabled = _s.FreelookEnabled;
        RescueShortcut = _s.RescueShortcut;
        ExcludedApps = string.Join(Environment.NewLine, _s.ExcludedApps);
        _s.Hotkeys.TryGetValue("show", out _showHotkey);
        _s.Hotkeys.TryGetValue("cursor.toggle", out _toggleCursorHotkey);
        _loading = false;
        RefreshWindowsState();
        LoadDesktop();
        _loading = true;
        StartWithWindows = StartupService.IsEnabled();
        _loading = false;
    }

    public string[] Algorithms { get; } = { "Cross", "Strait" };

    // Startup
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _enableCursorOnStart;

    // Cursor engine
    [ObservableProperty] private string _algorithm = "Cross";
    [ObservableProperty] private bool _loopX;
    [ObservableProperty] private bool _loopY;
    [ObservableProperty] private double _maxTravelDistance = 200;
    [ObservableProperty] private bool _allowDiscontinuity;
    [ObservableProperty] private bool _allowOverlaps;
    [ObservableProperty] private bool _adjustPointer = true;
    [ObservableProperty] private bool _adjustSpeed;
    [ObservableProperty] private bool _freelookEnabled = true;
    [ObservableProperty] private string _rescueShortcut = "Ctrl+Alt+Shift+M";
    [ObservableProperty] private string _excludedApps = "";
    [ObservableProperty] private bool _cursorDirty;

    // Hotkeys
    [ObservableProperty] private string? _showHotkey;
    [ObservableProperty] private string? _toggleCursorHotkey;

    // Windows desktop tweaks (read live from the registry)
    [ObservableProperty] private bool _searchHighlights;
    [ObservableProperty] private int _searchBoxMode;
    [ObservableProperty] private bool _widgets;
    [ObservableProperty] private bool _taskView;
    [ObservableProperty] private bool _chat;
    [ObservableProperty] private bool _copilot;
    [ObservableProperty] private bool _centerTaskbar;
    [ObservableProperty] private bool _accentOnTaskbar;
    public string[] SearchBoxModes { get; } = { "Hidden", "Icon only", "Search box", "Icon and label" };

    partial void OnSearchHighlightsChanged(bool v) => Tweak(() => WindowsThemeService.SetSearchHighlights(v));
    partial void OnSearchBoxModeChanged(int v) => Tweak(() => WindowsThemeService.SetSearchBoxMode(v));
    partial void OnWidgetsChanged(bool v) => Tweak(() => WindowsThemeService.SetWidgets(v));
    partial void OnTaskViewChanged(bool v) => Tweak(() => WindowsThemeService.SetTaskView(v));
    partial void OnChatChanged(bool v) => Tweak(() => WindowsThemeService.SetChat(v));
    partial void OnCopilotChanged(bool v) => Tweak(() => WindowsThemeService.SetCopilot(v));
    partial void OnCenterTaskbarChanged(bool v) => Tweak(() => WindowsThemeService.SetCenterTaskbar(v));
    partial void OnAccentOnTaskbarChanged(bool v) => Tweak(() => WindowsThemeService.SetAccentOnTaskbar(v));

    private void Tweak(Action apply)
    {
        if (_loading) return;
        try { apply(); Status = "Windows updated. If the taskbar didn't change, press Restart Explorer."; }
        catch (Exception ex) { Status = $"could not change Windows: {ex.Message}"; }
    }

    private void LoadDesktop()
    {
        try
        {
            var d = WindowsThemeService.ReadDesktop();
            _loading = true;
            SearchHighlights = d.SearchHighlights;
            SearchBoxMode = Math.Clamp(d.SearchBoxMode, 0, 3);
            Widgets = d.Widgets;
            TaskView = d.TaskView;
            Chat = d.Chat;
            Copilot = d.Copilot;
            CenterTaskbar = d.CenterTaskbar;
            AccentOnTaskbar = d.AccentOnTaskbar;
        }
        catch (Exception ex)
        {
            Status = $"could not read Windows settings: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    [RelayCommand]
    private void RestartExplorer()
    {
        try { WindowsThemeService.RestartExplorer(); Status = "Explorer restarted."; }
        catch (Exception ex) { Status = $"could not restart Explorer: {ex.Message}"; }
    }

    [RelayCommand]
    private void CleanTaskbar()
    {
        _loading = true;
        try
        {
            WindowsThemeService.SetSearchHighlights(false); SearchHighlights = false;
            WindowsThemeService.SetSearchBoxMode(1); SearchBoxMode = 1;
            WindowsThemeService.SetWidgets(false); Widgets = false;
            WindowsThemeService.SetTaskView(false); TaskView = false;
            WindowsThemeService.SetChat(false); Chat = false;
            WindowsThemeService.SetCopilot(false); Copilot = false;
            Status = "Taskbar cleaned: no search highlights, icon-only search, no Widgets, Task View, Chat, or Copilot.";
        }
        catch (Exception ex) { Status = $"could not clean taskbar: {ex.Message}"; }
        finally { _loading = false; }
    }

    // Windows skin
    [ObservableProperty] private string _windowsThemeState = "";
    [ObservableProperty] private string _status = "";

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        var (ok, msg) = value ? StartupService.Enable() : StartupService.Disable();
        Status = msg;
        if (!ok) { _loading = true; StartWithWindows = StartupService.IsEnabled(); _loading = false; }
    }

    partial void OnStartMinimizedChanged(bool value) => SaveSimple(() => _s.StartMinimized = value);
    partial void OnCloseToTrayChanged(bool value) => SaveSimple(() => _s.CloseToTray = value);
    partial void OnEnableCursorOnStartChanged(bool value) => SaveSimple(() => _s.EnableCursorOnStart = value);

    partial void OnAlgorithmChanged(string value) => MarkCursor();
    partial void OnLoopXChanged(bool value) => MarkCursor();
    partial void OnLoopYChanged(bool value) => MarkCursor();
    partial void OnMaxTravelDistanceChanged(double value) => MarkCursor();
    partial void OnAllowDiscontinuityChanged(bool value) => MarkCursor();
    partial void OnAllowOverlapsChanged(bool value) => MarkCursor();
    partial void OnAdjustPointerChanged(bool value) => MarkCursor();
    partial void OnAdjustSpeedChanged(bool value) => MarkCursor();
    partial void OnFreelookEnabledChanged(bool value) => MarkCursor();
    partial void OnRescueShortcutChanged(string value) => MarkCursor();
    partial void OnExcludedAppsChanged(string value) => MarkCursor();

    partial void OnShowHotkeyChanged(string? value) => SaveHotkey("show", value);
    partial void OnToggleCursorHotkeyChanged(string? value) => SaveHotkey("cursor.toggle", value);

    private void SaveSimple(Action set)
    {
        if (_loading) return;
        set();
        _s.Save();
    }

    private void SaveHotkey(string action, string? gesture)
    {
        if (_loading) return;
        if (string.IsNullOrEmpty(gesture)) _s.Hotkeys.Remove(action); else _s.Hotkeys[action] = gesture;
        _s.Save();
        _hotkeysChanged();
    }

    private void MarkCursor()
    {
        if (!_loading) CursorDirty = true;
    }

    [RelayCommand]
    private async Task ApplyCursorAsync()
    {
        _s.Algorithm = Algorithm;
        _s.LoopX = LoopX;
        _s.LoopY = LoopY;
        _s.MaxTravelDistance = MaxTravelDistance;
        _s.AllowDiscontinuity = AllowDiscontinuity;
        _s.AllowOverlaps = AllowOverlaps;
        _s.AdjustPointer = AdjustPointer;
        _s.AdjustSpeed = AdjustSpeed;
        _s.FreelookEnabled = FreelookEnabled;
        _s.RescueShortcut = RescueShortcut.Trim();
        _s.ExcludedApps = ExcludedApps.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        _s.Save();
        try
        {
            await _cursorOptionsChanged();
            CursorDirty = false;
            Status = "cursor options applied";
        }
        catch (Exception ex)
        {
            Status = $"could not apply cursor options: {ex.Message}";
        }
    }

    private void RefreshWindowsState()
    {
        try
        {
            var (dark, prevalence) = WindowsThemeService.Read();
            WindowsThemeState = dark
                ? (prevalence ? "Windows: dark, accent on title bars" : "Windows: dark, default accent")
                : "Windows: light mode";
        }
        catch (Exception ex)
        {
            WindowsThemeState = $"Windows: unknown ({ex.Message})";
        }
    }

    [RelayCommand]
    private void MatchWindows()
    {
        try
        {
            WindowsThemeService.ApplyMonochromeDark();
            Status = "Windows set to dark with a white accent. Some apps pick it up after a restart.";
        }
        catch (Exception ex)
        {
            Status = $"could not change Windows theme: {ex.Message}";
        }
        RefreshWindowsState();
    }
}
