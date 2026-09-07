using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pixelscreens.Services;

namespace Pixelscreens.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel()
    {
        RefreshWindowsState();
    }

    [ObservableProperty] private string _windowsThemeState = "";
    [ObservableProperty] private string _status = "";

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
