using CommunityToolkit.Mvvm.ComponentModel;
using Pixelscreens.Services;

namespace Pixelscreens.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly CursorEngineService _cursorEngine = new();

    public MainViewModel()
    {
        Profiles = new ProfilesViewModel(new DisplayProfileService());
        Layout = new LayoutViewModel(_cursorEngine);
        Settings = new SettingsViewModel();
        Profiles.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(ProfilesViewModel.Status) or nameof(ProfilesViewModel.EngineOk)) RaiseFooter(); };
        Layout.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(LayoutViewModel.Status) or nameof(LayoutViewModel.EngineOk)) RaiseFooter(); };
    }

    public ProfilesViewModel Profiles { get; }
    public LayoutViewModel Layout { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private int _selectedTab;

    public bool ShowProfiles => SelectedTab == 0;
    public bool ShowLayout => SelectedTab == 1;
    public bool ShowSettings => SelectedTab == 2;

    public string FooterStatus => SelectedTab == 1 ? Layout.Status : Profiles.Status;
    public bool FooterOk => SelectedTab == 1 ? Layout.EngineOk : Profiles.EngineOk;

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(ShowProfiles));
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
        await Layout.RefreshAsync();
    }

    public void Shutdown()
    {
        try { _cursorEngine.Shutdown(); } catch { /* best effort on exit */ }
    }

    public void Dispose() => _cursorEngine.Dispose();
}
