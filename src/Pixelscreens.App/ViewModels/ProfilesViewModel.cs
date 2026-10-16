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

    /// <summary>Raised when a profile should be shown on the Layout tab for editing.</summary>
    public event Action? OpenLayoutRequested;

    /// <summary>The profile open on the Layout tab. Null means the live arrangement isn't saved as a profile.</summary>
    [ObservableProperty] private ProfileRow? _editing;
    [ObservableProperty] private string _editName = "";

    public bool HasEditing => Editing is not null;
    /// <summary>The profile being edited isn't on screen, so only its settings can change until it is applied.</summary>
    public bool EditingOffline => Editing is { IsActive: false };
    public bool EditingLive => !EditingOffline;
    public string EditingTitle => Editing?.Name ?? "Unsaved layout";

    partial void OnEditingChanged(ProfileRow? value)
    {
        EditName = value?.Name ?? "";
        OnPropertyChanged(nameof(HasEditing));
        OnPropertyChanged(nameof(EditingOffline));
        OnPropertyChanged(nameof(EditingLive));
        OnPropertyChanged(nameof(EditingTitle));
    }

    /// <summary>Open the profile that is on screen (or the unsaved live layout).</summary>
    [RelayCommand]
    private void OpenLayout() => BeginEdit(Profiles.FirstOrDefault(r => r.IsActive));

    public void EditActiveIfNone()
    {
        if (Editing is null) Editing = Profiles.FirstOrDefault(r => r.IsActive);
    }

    internal void BeginEdit(ProfileRow? row)
    {
        Editing = row;
        OpenLayoutRequested?.Invoke();
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (Editing is not { } row) return;
        var name = EditName.Trim();
        if (name.Length == 0 || name == row.Name) return;
        var ok = await _service.RenameAsync(row.Item, name);
        Status = ok ? $"renamed to \"{name}\"" : $"could not rename (is \"{name}\" already used?)";
        await ReloadAsync(keepStatus: true);
    }

    [RelayCommand]
    private async Task ApplyEditingAsync()
    {
        if (Editing is { CanApply: true } row) await ApplyAsync(row);
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
            var currentScreens = ScreenRect.From(current, inv);
            _currentRaw = currentScreens;

            Profiles.Clear();
            foreach (var p in items)
            {
                Profiles.Add(new ProfileRow(p, _service.IsActive(p), inv, currentScreens, this, _settings));
            }

            var match = Profiles.FirstOrDefault(r => r.IsActive);
            if (Editing is { } was) Editing = Profiles.FirstOrDefault(r => r.Uuid == was.Uuid);
            Physicalize();
            CurrentIsSaved = match is not null;
            CurrentTitle = match is not null ? match.Name : "Unsaved layout";
            CurrentDetail = currentScreens.Count == 0 ? "no active displays reported" :
                string.Join("\n", currentScreens
                    .OrderByDescending(r => r.IsPrimary).ThenBy(r => r.X)
                    .Select(r => $"{r.Label}  ·  {r.W}x{r.H}  ·  {r.Orientation}" + (r.IsPrimary ? "  ·  primary" : "")));
            var idle = DisplayInventory.Except(inv.Connected, inv.Active);
            CurrentIdle = idle.Count == 0 ? "" : "Off: " + string.Join(", ", idle);

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

    private IReadOnlyList<ScreenRect> _currentRaw = [];
    private IReadOnlyDictionary<string, DeskScreen> _desk = new Dictionary<string, DeskScreen>();

    /// <summary>The Layout tab changed (rescan, drag, resize): redraw every preview from it.</summary>
    public void SetDesk(IEnumerable<DeskScreen> screens)
    {
        _desk = screens.Where(d => d.Pnp.Length > 0).GroupBy(d => d.Pnp).ToDictionary(g => g.Key, g => g.First());
        // Remember sizes so profiles still draw true-to-size when a monitor is unplugged.
        var changed = false;
        foreach (var d in _desk.Values)
        {
            var mm = new[] { Math.Round(d.W, 1), Math.Round(d.H, 1) };
            if (_settings.MonitorSizesMm.TryGetValue(d.Pnp, out var old) && old.SequenceEqual(mm)) continue;
            _settings.MonitorSizesMm[d.Pnp] = mm;
            changed = true;
        }
        if (changed) _settings.Save();
        Physicalize();
    }

    private void Physicalize()
    {
        CurrentScreens = PhysicalPreview.Apply(_currentRaw, _desk, _settings.MonitorSizesMm, useLive: true);
        foreach (var r in Profiles) r.Screens = PhysicalPreview.Apply(r.RawScreens, _desk, _settings.MonitorSizesMm, useLive: r.IsActive);
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
            _editNewUuid = saved?.UUID;
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
        if (_editNewUuid is { } id && Editing is null) Editing = Profiles.FirstOrDefault(r => r.Uuid == id);
        _editNewUuid = null;
    }

    private string? _editNewUuid;

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
            if (ok && Editing?.Uuid == row.Uuid) Editing = null;
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

public enum ScreenState { Unknown, Present, Missing }

public sealed record ScreenRect(int X, int Y, int W, int H, bool IsPrimary, string Name)
{
    public string Label { get; init; } = "";
    public string Pnp { get; init; } = "";
    public ScreenState State { get; init; }

    // Desk position and size in millimetres (see PhysicalPreview). Previews draw these when set.
    public double PX { get; init; }
    public double PY { get; init; }
    public double PW { get; init; }
    public double PH { get; init; }
    public bool HasPhys { get; init; }
    public ScreenRotation Rotation { get; init; }

    public string Orientation => (W >= H ? "landscape" : "portrait") +
        (Rotation is ScreenRotation.ROTATE_180 or ScreenRotation.ROTATE_270 ? " (flipped)" : "");

    /// <summary>The profile's screens, each tagged with its monitor and whether that monitor is plugged in now.</summary>
    public static IReadOnlyList<ScreenRect> From(ProfileItem? item, DisplayInventory inv)
    {
        if (item is null) return [];
        try
        {
            var monitors = DisplayInventory.ScreenMonitors(item);
            var connected = inv.Connected.Select(r => r.Pnp).ToHashSet();
            return item.GetScreenPositions()
                .Select(sp =>
                {
                    var known = monitors.TryGetValue(sp.Name ?? "", out var mon);
                    return new ScreenRect(sp.ScreenX, sp.ScreenY, sp.ScreenWidth, sp.ScreenHeight, sp.IsPrimary, sp.Name ?? "")
                    {
                        Label = known ? mon!.ToString() : $"{sp.ScreenWidth}x{sp.ScreenHeight}",
                        Pnp = known ? mon!.Pnp : "",
                        State = !known ? ScreenState.Unknown : connected.Contains(mon!.Pnp) ? ScreenState.Present : ScreenState.Missing,
                        Rotation = sp.Rotation,
                    };
                })
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
        RawScreens = ScreenRect.From(item, inv);
        _screens = RawScreens;

        var wants = DisplayInventory.Of(item);
        Missing = DisplayInventory.Except(wants, inv.Connected);
        var turnsOff = DisplayInventory.Except(inv.Active, wants);
        var turnsOn = DisplayInventory.Except(DisplayInventory.Except(wants, inv.Active), Missing);

        // DisplayMagician's strict equality misses profiles saved by older builds, so also accept
        // "same monitors on, same pixel arrangement".
        IsActive = engineSaysActive ||
            (wants.Count > 0 && DisplayInventory.SameSet(wants, inv.Active) && ScreenRect.SameArrangement(RawScreens, currentScreens));

        IsUsable = item.HasUsableSavedConfiguration(out var msg) && Missing.Count == 0;
        if (Missing.Count > 0)
            Problem = "Missing: " + string.Join(", ", Missing) + " (not plugged in)";
        else if (!IsUsable)
            Problem = string.IsNullOrWhiteSpace(msg) ? "This profile can't be applied." : msg.Trim();

        // One short line under the name; the full list lives in the tooltip.
        if (IsActive)
        {
            Note = "On screen now";
            NoteIsGood = true;
        }
        else if (Missing.Count > 0)
        {
            Note = Missing.Count == 1 ? $"Needs {Missing[0]}" : $"Needs {Missing.Count} unplugged monitors";
            NoteIsBad = true;
            NoteDetail = Problem;
        }
        else if (!IsUsable)
        {
            Note = "Can't be applied";
            NoteIsBad = true;
            NoteDetail = Problem;
        }
        else if (!IsActive)
        {
            var on = turnsOn.Count; var off = turnsOff.Count;
            Note = (on, off) switch
            {
                (0, 0) => "Same monitors, new arrangement",
                (0, _) => off == 1 ? $"Turns off {turnsOff[0]}" : $"Turns off {off} monitors",
                (_, 0) => on == 1 ? $"Turns on {turnsOn[0]}" : $"Turns on {on} monitors",
                _ => $"Turns on {on}, off {off}",
            };
            NoteDetail = string.Join("\n", turnsOn.Select(m => $"on: {m}").Concat(turnsOff.Select(m => $"off: {m}")));
        }

        if (!IsActive && Missing.Count == 0)
        {
            var parts = new List<string>();
            if (turnsOn.Count > 0) parts.Add("turns on " + string.Join(", ", turnsOn));
            if (turnsOff.Count > 0) parts.Add("turns off " + string.Join(", ", turnsOff));
            if (parts.Count == 0) parts.Add("same monitors, different arrangement");
            Changes = string.Join(" · ", parts);
        }

        var names = wants.Count > 0 ? string.Join(", ", wants) : string.Join(", ", RawScreens.Select(sc => $"{sc.W}x{sc.H}"));
        Summary = RawScreens.Count == 0 ? names : $"{RawScreens.Count} screen(s) · {names}";
    }

    /// <summary>Pixel arrangement as Windows stores it.</summary>
    public IReadOnlyList<ScreenRect> RawScreens { get; }
    /// <summary>What the preview draws: the same screens at desk-true size and position.</summary>
    [ObservableProperty] private IReadOnlyList<ScreenRect> _screens;
    public IReadOnlyList<MonitorRef> Missing { get; }
    public string Problem { get; } = "";
    public bool HasProblem => Problem.Length > 0;
    public string Changes { get; } = "";
    public string Note { get; } = "";
    public bool NoteIsBad { get; }
    public bool NoteIsGood { get; }
    public string? NoteDetail { get; }
    public bool HasNote => Note.Length > 0;
    public string StartupTip => IsStartup ? "Applied every time PixelScreens starts. Click to turn off." : "Apply this profile every time PixelScreens starts.";
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
    public string StartupLabel => IsStartup ? "STARTUP ON" : "STARTUP";

    public string Badge => IsActive ? "ACTIVE" : Missing.Count > 0 ? "MISSING DISPLAY" : IsUsable ? "" : "UNAVAILABLE";
    public bool HasBadge => Badge.Length > 0;
    public bool CanApply => IsUsable && !IsActive;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyAsync() => _owner.ApplyAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(this);

    [RelayCommand]
    private Task ToggleStartupAsync() => _owner.ToggleStartupAsync(this);

    /// <summary>A profile is a layout: Edit opens it on the Layout tab with its settings.</summary>
    [RelayCommand]
    private void Edit() => _owner.BeginEdit(this);
}
