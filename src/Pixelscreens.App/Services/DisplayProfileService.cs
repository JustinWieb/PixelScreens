using DisplayMagicianShared;

namespace Pixelscreens.Services;

/// <summary>
/// Thin wrapper over the DisplayMagician profile engine so the UI never touches its statics directly.
/// All calls that talk to the GPU drivers run on a worker thread; callers marshal back to the UI.
/// </summary>
public sealed class DisplayProfileService
{
    private bool _initialised;

    public IReadOnlyList<ProfileItem> Load()
    {
        if (!_initialised)
        {
            ProfileRepository.InitialiseRepository();
            _initialised = true;
        }
        else
        {
            ProfileRepository.UpdateActiveProfile(fastScan: false);
        }
        return ProfileRepository.AllProfiles.ToList();
    }

    public ProfileItem? Current => _initialised ? ProfileRepository.CurrentProfile : null;

    public bool IsActive(ProfileItem profile) => ProfileRepository.IsActiveProfile(profile);

    public Task<ApplyProfileResult> ApplyAsync(ProfileItem profile) =>
        Task.Run(() => ProfileRepository.ApplyProfile(profile));

    /// <summary>Capture the live monitor arrangement and store it under <paramref name="name"/>.</summary>
    public Task<ProfileItem?> SaveCurrentAsync(string name) => Task.Run(() =>
    {
        var profile = new ProfileItem { Name = name.Trim() };
        if (!profile.CreateProfileFromCurrentDisplaySettings(captureWallpaper: false)) return null;
        return ProfileRepository.AddProfile(profile) ? profile : null;
    });

    public Task<bool> DeleteAsync(ProfileItem profile) =>
        Task.Run(() => ProfileRepository.RemoveProfile(profile));
}
