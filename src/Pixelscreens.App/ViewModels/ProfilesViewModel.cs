using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayMagicianShared;
using Pixelscreens.Services;

namespace Pixelscreens.ViewModels;

public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly DisplayProfileService _service;
    private readonly AppSettings _settings;
    private readonly Action _hotkeysChanged;

    public ProfilesViewModel(DisplayProfileService service, AppSettings settings, Action hotkeysChanged)
    {
        _service = service;
        _settings = settings;
        _hotkeysChanged = hotkeysChanged;
    }

    /// <summary>Raised after a profile apply actually changed the Windows arrangement.</summary>
    public event Action? DisplaysChanged;

    public async Task ApplyByUuidAsync(string uuid)
    {
        var row = Profiles.FirstOrDefault(r => r.Uuid == uuid);
        if (row is not null && row.CanApply) await ApplyAsync(row);
    }

    internal void HotkeyChanged(ProfileRow row, string? gesture)
    {
        var key = $"profile:{row.Uuid}";
        if (string.IsNullOrEmpty(gesture)) _settings.Hotkeys.Remove(key); else _settings.Hotkeys[key] = gesture;
        _settings.Save();
        _hotkeysChanged();
    }

    public ObservableCollection<ProfileRow> Profiles { get; } = new();

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _engineOk;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _newProfileName = "";

    // Live arrangement card.
    [ObservableProperty] private IReadOnlyList<ScreenRect> _currentScreens = [];
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private string _currentDetail = "";
    [ObservableProperty] private string _currentIdle = "";
    [ObservableProperty] private bool _currentIsSaved;

    public bool HasCurrentIdle => CurrentIdle.Length > 0;
    partial void OnCurrentIdleChanged(string value) => OnPropertyChanged(nameof(HasCurrentIdle));

    [RelayCommand]
    public Task RefreshAsync() => ReloadAsync(keepStatus: false);

    /// <summary>Re-read after Windows changed displays, without overwriting the last action's status.</summary>
    public Task SyncAsync() => Busy ? Task.CompletedTask : ReloadAsync(keepStatus: true);

    private async Task ReloadAsync(bool keepStatus)
    {
        Busy = true;
        try
        {
            var (items, current, inv) = await Task.Run(() => _service.Snapshot());
            var currentScreens = ScreenRect.From(current);

            Profiles.Clear();
            foreach (var p in items)
            {
                Profiles.Add(new ProfileRow(p, _service.IsActive(p), inv, currentScreens, this, _settings));
            }

            var match = Profiles.FirstOrDefault(r => r.IsActive);
            CurrentScreens = currentScreens;
            CurrentIsSaved = match is not null;
            CurrentTitle = match is not null ? match.Name : "Not saved as a profile";
            CurrentDetail = inv.Active.Count == 0 ? "no active displays reported" :
                $"{inv.Active.Count} on: " + string.Join(", ", inv.Active);
            var idle = DisplayInventory.Except(inv.Connected, inv.Active);
            CurrentIdle = idle.Count == 0 ? "" : "plugged in but off: " + string.Join(", ", idle);

            EngineOk = true;
            if (!keepStatus) Status = $"display engine ok · {Profiles.Count} profile(s)" + (match is null ? " · current layout is unsaved" : "");
        }
        catch (Exception ex)
        {
            EngineOk = false;
            Status = $"display engine failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    internal bool IsStartup(string uuid) => _settings.StartupProfileUuid == uuid;

    internal async Task ToggleStartupAsync(ProfileRow row)
    {
        var on = !row.IsStartup;
        _settings.StartupProfileUuid = on ? row.Uuid : null;
        _settings.Save();
        Status = on ? $"\"{row.Name}\" will be applied every time PixelScreens starts" : "no profile applied at startup";
        await ReloadAsync(keepStatus: true);
    }

    /// <summary>
    /// Put the startup profile on screen. At sign-in some monitors (TVs, DisplayPort sleep) wake late,
    /// so wait up to <paramref name="wait"/> for them before applying.
    /// </summary>
    public async Task ApplyStartupProfileAsync(TimeSpan wait)
    {
        var uuid = _settings.StartupProfileUuid;
        if (string.IsNullOrEmpty(uuid)) return;
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            var row = Profiles.FirstOrDefault(r => r.Uuid == uuid);
            if (row is null) { Status = "startup profile no longer exists; pick another on Profiles"; return; }
            if (row.IsActive) { Status = $"startup: \"{row.Name}\" already on screen"; return; }
            if (row.CanApply) { await ApplyAsync(row); return; }
            if (DateTime.UtcNow >= deadline)
            {
                Status = $"startup: could not apply \"{row.Name}\". {row.Problem}";
                return;
            }
            Status = $"startup: waiting for monitors before applying \"{row.Name}\"... {row.Problem}";
            await Task.Delay(3000);
            await ReloadAsync(keepStatus: true);
        }
    }

    [RelayCommand]
    private async Task SaveCurrentAsync()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0) name = $"Profile {DateTime.Now:yyyy-MM-dd HH:mm}";
        Busy = true;
        Status = $"capturing current layout as \"{name}\"...";
        try
        {
            var saved = await _service.SaveCurrentAsync(name);
            Status = saved is null ? "save failed: engine could not capture the current layout" : $"saved \"{saved.Name}\"";
            NewProfileName = "";
        }
        catch (Exception ex)
        {
            Status = $"save failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
        await ReloadAsync(keepStatus: true);
    }

    internal async Task ApplyAsync(ProfileRow row)
    {
        Busy = true;
        Status = $"applying \"{row.Name}\"...";
        ApplyProfileResult? result = null;
        try
        {
            result = await _service.ApplyAsync(row.Item);
        }
        catch (Exception ex)
        {
            Status = $"apply failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }

        // Windows settles the new arrangement asynchronously; give it a moment before re-reading.
        if (result == ApplyProfileResult.Successful) await Task.Delay(1500);
        await ReloadAsync(keepStatus: true);

        var now = Profiles.FirstOrDefault(r => r.Uuid == row.Uuid);
        if (result is not null)
        {
            Status = result switch
            {
                ApplyProfileResult.Successful when now?.IsActive == true => $"applied \"{row.Name}\"",
                ApplyProfileResult.Successful => $"applied \"{row.Name}\", but Windows now shows a different arrangement",
                ApplyProfileResult.Cancelled => $"apply cancelled for \"{row.Name}\"",
                _ => $"apply failed for \"{row.Name}\"" + (now?.HasProblem == true ? $": {now.Problem}" : ""),
            };
        }
        if (result == ApplyProfileResult.Successful) DisplaysChanged?.Invoke();
    }

    internal async Task DeleteAsync(ProfileRow row)
    {
        Busy = true;
        try
        {
            var ok = await _service.DeleteAsync(row.Item);
            Status = ok ? $"deleted \"{row.Name}\"" : $"could not delete \"{row.Name}\"";
            if (ok) { _settings.Hotkeys.Remove($"profile:{row.Uuid}"); _settings.Save(); _hotkeysChanged(); }
        }
        catch (Exception ex)
        {
            Status = $"delete failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
        await ReloadAsync(keepStatus: true);
    }
}

public sealed record ScreenRect(int X, int Y, int W, int H, bool IsPrimary, string Name)
{
    public static IReadOnlyList<ScreenRect> From(ProfileItem? item)
    {
        if (item is null) return [];
        try
        {
            return item.GetScreenPositions()
                .Select(sp => new ScreenRect(sp.ScreenX, sp.ScreenY, sp.ScreenWidth, sp.ScreenHeight, sp.IsPrimary, sp.Name ?? ""))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Same screens at the same pixel positions, ignoring order and names.</summary>
    public static bool SameArrangement(IReadOnlyList<ScreenRect> a, IReadOnlyList<ScreenRect> b)
    {
        if (a.Count == 0 || a.Count != b.Count) return false;
        static IEnumerable<(int, int, int, int, bool)> Key(IEnumerable<ScreenRect> s) =>
            s.Select(r => (r.X, r.Y, r.W, r.H, r.IsPrimary)).Order();
        return Key(a).SequenceEqual(Key(b));
    }
}

public sealed partial class ProfileRow : ObservableObject
{
    private readonly ProfilesViewModel _owner;

    public ProfileRow(ProfileItem item, bool engineSaysActive, DisplayInventory inv, IReadOnlyList<ScreenRect> currentScreens,
        ProfilesViewModel owner, AppSettings settings)
    {
        Item = item;
        _owner = owner;
        IsStartup = owner.IsStartup(item.UUID);
        settings.Hotkeys.TryGetValue($"profile:{Uuid}", out _hotkey);
        Screens = ScreenRect.From(item);

        var wants = DisplayInventory.Of(item);
        Missing = DisplayInventory.Except(wants, inv.Connected);
        var turnsOff = DisplayInventory.Except(inv.Active, wants);
        var turnsOn = DisplayInventory.Except(DisplayInventory.Except(wants, inv.Active), Missing);

        // DisplayMagician's strict equality misses profiles saved by older builds, so also accept
        // "same monitors on, same pixel arrangement".
        IsActive = engineSaysActive ||
            (wants.Count > 0 && DisplayInventory.SameSet(wants, inv.Active) && ScreenRect.SameArrangement(Screens, currentScreens));

        IsUsable = item.HasUsableSavedConfiguration(out var msg) && Missing.Count == 0;
        if (Missing.Count > 0)
            Problem = "Missing: " + string.Join(", ", Missing) + " (not plugged in)";
        else if (!IsUsable)
            Problem = string.IsNullOrWhiteSpace(msg) ? "This profile can't be applied." : msg.Trim();

        if (!IsActive && Missing.Count == 0)
        {
            var parts = new List<string>();
            if (turnsOn.Count > 0) parts.Add("turns on " + string.Join(", ", turnsOn));
            if (turnsOff.Count > 0) parts.Add("turns off " + string.Join(", ", turnsOff));
            if (parts.Count == 0) parts.Add("same monitors, different arrangement");
            Changes = string.Join(" · ", parts);
        }

        var names = wants.Count > 0 ? string.Join(", ", wants) : string.Join(", ", Screens.Select(sc => $"{sc.W}x{sc.H}"));
        Summary = Screens.Count == 0 ? names : $"{Screens.Count} screen(s) · {names}";
    }

    public IReadOnlyList<ScreenRect> Screens { get; }
    public IReadOnlyList<MonitorRef> Missing { get; }
    public string Problem { get; } = "";
    public bool HasProblem => Problem.Length > 0;
    public string Changes { get; } = "";
    public bool HasChanges => Changes.Length > 0;
    public string Summary { get; }

    [ObservableProperty] private string? _hotkey;
    partial void OnHotkeyChanged(string? value) => _owner.HotkeyChanged(this, value);

    public ProfileItem Item { get; }
    public string Name => Item.Name;
    public string Uuid => Item.UUID;
    public bool IsActive { get; }
    public bool IsUsable { get; }
    public bool IsStartup { get; }
    public string StartupLabel => IsStartup ? "Startup: on" : "Use at startup";

    public string Badge => IsActive ? "ACTIVE" : Missing.Count > 0 ? "MISSING DISPLAY" : IsUsable ? "" : "UNAVAILABLE";
    public bool HasBadge => Badge.Length > 0;
    public bool CanApply => IsUsable && !IsActive;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyAsync() => _owner.ApplyAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(this);

    [RelayCommand]
    private Task ToggleStartupAsync() => _owner.ToggleStartupAsync(this);
}
