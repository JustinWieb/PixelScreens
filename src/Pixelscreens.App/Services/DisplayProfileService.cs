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

    /// <summary>Profiles plus a fresh read of the live arrangement and plugged-in monitors.</summary>
    public (IReadOnlyList<ProfileItem> Profiles, ProfileItem? Current, DisplayInventory Inventory) Snapshot()
    {
        var profiles = Load();
        return (profiles, Current, DisplayInventory.Capture());
    }

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

    /// <summary>
    /// Arrange live monitors into <paramref name="placements"/>, then re-capture that as <paramref name="old"/>,
    /// keeping its name and id (so hotkeys and the startup choice still point at it).
    /// </summary>
    public Task<(bool ok, string message)> RebuildAsync(ProfileItem old, IReadOnlyList<DisplayArranger.Placement> placements) => Task.Run(async () =>
    {
        var (ok, msg) = DisplayArranger.Apply(placements);
        if (!ok) return (false, msg);
        await Task.Delay(2500); // let Windows and the driver settle before reading the result back

        var fresh = new ProfileItem { Name = old.Name };
        if (!fresh.CreateProfileFromCurrentDisplaySettings(captureWallpaper: false))
            return (false, "monitors arranged, but the new layout couldn't be captured");
        fresh.UUID = old.UUID;

        var twin = ProfileRepository.AllProfiles.FirstOrDefault(p => !ReferenceEquals(p, old) && p.Equals(fresh));
        if (twin is not null) return (false, $"monitors arranged, but that's identical to \"{twin.Name}\", so nothing was overwritten");

        if (!ProfileRepository.RemoveProfile(old)) return (false, "monitors arranged, but the old profile couldn't be replaced");
        return ProfileRepository.AddProfile(fresh) ? (true, "rebuilt") : (false, "monitors arranged, but saving the profile failed");
    });

    public Task<bool> RenameAsync(ProfileItem profile, string name) =>
        Task.Run(() => ProfileRepository.RenameProfile(profile, name.Trim()));

    public Task<bool> DeleteAsync(ProfileItem profile) =>
        Task.Run(() => ProfileRepository.RemoveProfile(profile));
}
