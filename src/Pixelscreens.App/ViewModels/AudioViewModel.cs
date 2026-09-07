using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayMagicianShared;
using Pixelscreens.Services;

namespace Pixelscreens.ViewModels;

public sealed partial class AudioViewModel : ObservableObject
{
    private readonly AudioProfileService _service;
    private readonly AppSettings _settings;
    private readonly Action _hotkeysChanged;

    public AudioViewModel(AudioProfileService service, AppSettings settings, Action hotkeysChanged)
    {
        _service = service;
        _settings = settings;
        _hotkeysChanged = hotkeysChanged;
    }

    public ObservableCollection<AudioRow> Profiles { get; } = new();

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
                Profiles.Add(new AudioRow(p, _service.IsActive(p), this, _settings));
            EngineOk = true;
            Status = $"{Profiles.Count} audio profile(s)";
        }
        catch (Exception ex)
        {
            EngineOk = false;
            Status = $"audio engine failed: {ex.GetType().Name}: {ex.Message}";
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
        if (name.Length == 0) name = $"Audio {DateTime.Now:yyyy-MM-dd HH:mm}";
        Busy = true;
        try
        {
            var saved = await _service.SaveCurrentAsync(name);
            Status = saved is null ? "save failed" : $"saved \"{saved.Name}\"";
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

    internal async Task ApplyAsync(AudioRow row)
    {
        Busy = true;
        Status = $"switching to \"{row.Name}\"...";
        try
        {
            var ok = await _service.ApplyAsync(row.Item);
            Status = ok ? $"switched to \"{row.Name}\"" : $"could not switch to \"{row.Name}\" (device missing?)";
        }
        catch (Exception ex)
        {
            Status = $"switch failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
        await RefreshAsync();
    }

    public async Task ApplyByUuidAsync(string uuid)
    {
        var row = Profiles.FirstOrDefault(r => r.Uuid == uuid);
        if (row is not null) await ApplyAsync(row);
    }

    internal async Task DeleteAsync(AudioRow row)
    {
        Busy = true;
        try
        {
            var ok = await _service.DeleteAsync(row.Item);
            Status = ok ? $"deleted \"{row.Name}\"" : $"could not delete \"{row.Name}\"";
            _settings.Hotkeys.Remove($"audio:{row.Uuid}");
            _settings.Save();
            _hotkeysChanged();
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

    internal void HotkeyChanged(AudioRow row, string? gesture)
    {
        var key = $"audio:{row.Uuid}";
        if (string.IsNullOrEmpty(gesture)) _settings.Hotkeys.Remove(key); else _settings.Hotkeys[key] = gesture;
        _settings.Save();
        _hotkeysChanged();
    }
}

public sealed partial class AudioRow : ObservableObject
{
    private readonly AudioViewModel _owner;

    public AudioRow(AudioProfileItem item, bool isActive, AudioViewModel owner, AppSettings settings)
    {
        Item = item;
        IsActive = isActive;
        _owner = owner;
        settings.Hotkeys.TryGetValue($"audio:{Uuid}", out _hotkey);
    }

    public AudioProfileItem Item { get; }
    public string Name => Item.Name;
    public string Uuid => Item.UUID;
    public string Detail
    {
        get
        {
            try { return Item.GenerateSettingsText().Replace("\r", "").Replace("\n", " · ").Trim(' ', '·'); }
            catch { return ""; }
        }
    }
    public bool IsActive { get; }
    public string Badge => IsActive ? "ACTIVE" : "";
    public bool HasBadge => IsActive;

    [ObservableProperty] private string? _hotkey;
    partial void OnHotkeyChanged(string? value) => _owner.HotkeyChanged(this, value);

    [RelayCommand] private Task ApplyAsync() => _owner.ApplyAsync(this);
    [RelayCommand] private Task DeleteAsync() => _owner.DeleteAsync(this);
}
