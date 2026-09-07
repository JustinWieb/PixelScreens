using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DisplayMagicianShared;

namespace Pixelscreens.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Window _window;

    public MainViewModel(Window window)
    {
        _window = window;
        Refresh();
    }

    public ObservableCollection<ProfileRow> Profiles { get; } = new();
    public ObservableCollection<ScreenRow> Screens { get; } = new();

    [ObservableProperty] private string _engineStatus = "";
    [ObservableProperty] private bool _engineOk;
    [ObservableProperty] private int _selectedTab;

    public bool ShowProfiles => SelectedTab == 0;
    public bool ShowLayout => SelectedTab == 1;
    public bool ShowSettings => SelectedTab == 2;

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(ShowProfiles));
        OnPropertyChanged(nameof(ShowLayout));
        OnPropertyChanged(nameof(ShowSettings));
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadProfiles();
        LoadScreens();
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        try
        {
            // DisplayMagician engine: reads %LOCALAPPDATA%\DisplayMagician\DisplayProfiles.json
            // and probes NVIDIA / AMD / Intel / Windows CCD for the live configuration.
            ProfileRepository.InitialiseRepository();
            var current = ProfileRepository.CurrentProfile;
            foreach (var p in ProfileRepository.AllProfiles)
            {
                Profiles.Add(new ProfileRow(
                    p.Name,
                    p.UUID,
                    current is not null && current.UUID == p.UUID,
                    p.HasUsableSavedConfiguration(out _)));
            }

            EngineOk = true;
            EngineStatus = $"display engine ok · {Profiles.Count} profile(s)";
        }
        catch (Exception ex)
        {
            EngineOk = false;
            EngineStatus = $"display engine failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void LoadScreens()
    {
        Screens.Clear();
        var screens = _window.Screens;
        if (screens is null) return;
        var i = 0;
        foreach (var s in screens.All)
        {
            i++;
            var b = s.Bounds;
            Screens.Add(new ScreenRow(
                s.DisplayName ?? $"Display {i}",
                $"{b.Width} x {b.Height}",
                $"{b.X}, {b.Y}",
                $"{s.Scaling * 100:0}%",
                s.IsPrimary));
        }
    }
}

public sealed record ProfileRow(string Name, string Uuid, bool IsActive, bool IsUsable)
{
    public string Badge => IsActive ? "ACTIVE" : IsUsable ? "" : "UNAVAILABLE";
    public bool HasBadge => Badge.Length > 0;
}

public sealed record ScreenRow(string Name, string Resolution, string Position, string Scale, bool IsPrimary)
{
    public string Badge => IsPrimary ? "PRIMARY" : "";
    public bool HasBadge => IsPrimary;
}
