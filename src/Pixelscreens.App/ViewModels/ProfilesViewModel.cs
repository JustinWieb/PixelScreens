using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayMagicianShared;
using Pixelscreens.Services;

namespace Pixelscreens.ViewModels;

public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly DisplayProfileService _service;

    public ProfilesViewModel(DisplayProfileService service)
    {
        _service = service;
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
                Profiles.Add(new ProfileRow(p, _service.IsActive(p), p.HasUsableSavedConfiguration(out _), this));
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

    public ProfileRow(ProfileItem item, bool isActive, bool isUsable, ProfilesViewModel owner)
    {
        Item = item;
        IsActive = isActive;
        IsUsable = isUsable;
        _owner = owner;
    }

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
