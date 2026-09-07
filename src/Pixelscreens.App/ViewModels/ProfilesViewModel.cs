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

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Busy = true;
        try
        {
            var items = await Task.Run(() => _service.Load());
            Profiles.Clear();
            foreach (var p in items)
            {
                Profiles.Add(new ProfileRow(p, _service.IsActive(p), p.HasUsableSavedConfiguration(out _), this, _settings));
            }
            EngineOk = true;
            Status = $"display engine ok · {Profiles.Count} profile(s)";
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
        await RefreshAsync();
    }

    internal async Task ApplyAsync(ProfileRow row)
    {
        Busy = true;
        Status = $"applying \"{row.Name}\"...";
        try
        {
            var result = await _service.ApplyAsync(row.Item);
            Status = result switch
            {
                ApplyProfileResult.Successful => $"applied \"{row.Name}\"",
                ApplyProfileResult.Cancelled => $"apply cancelled for \"{row.Name}\"",
                _ => $"apply failed for \"{row.Name}\"",
            };
        }
        catch (Exception ex)
        {
            Status = $"apply failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
        await RefreshAsync();
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
        await RefreshAsync();
    }
}

public sealed partial class ProfileRow : ObservableObject
{
    private readonly ProfilesViewModel _owner;

    public ProfileRow(ProfileItem item, bool isActive, bool isUsable, ProfilesViewModel owner, AppSettings settings)
    {
        Item = item;
        IsActive = isActive;
        IsUsable = isUsable;
        _owner = owner;
        settings.Hotkeys.TryGetValue($"profile:{Uuid}", out _hotkey);
    }

    [ObservableProperty] private string? _hotkey;
    partial void OnHotkeyChanged(string? value) => _owner.HotkeyChanged(this, value);

    public ProfileItem Item { get; }
    public string Name => Item.Name;
    public string Uuid => Item.UUID;
    public bool IsActive { get; }
    public bool IsUsable { get; }
    public string Badge => IsActive ? "ACTIVE" : IsUsable ? "" : "UNAVAILABLE";
    public bool HasBadge => Badge.Length > 0;
    public bool CanApply => IsUsable && !IsActive;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyAsync() => _owner.ApplyAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(this);
}
