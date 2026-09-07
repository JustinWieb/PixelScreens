using DisplayMagicianShared;

namespace Pixelscreens.Services;

/// <summary>Wrapper over DisplayMagician's audio profile engine (default speaker, mic, and volumes).</summary>
public sealed class AudioProfileService
{
    private bool _initialised;

    public IReadOnlyList<AudioProfileItem> Load()
    {
        if (!_initialised)
        {
            AudioProfileRepository.InitialiseRepository();
            _initialised = true;
        }
        else
        {
            AudioProfileRepository.UpdateActiveAudioProfile();
        }
        return AudioProfileRepository.AllAudioProfiles.ToList();
    }

    public bool IsActive(AudioProfileItem p) => AudioProfileRepository.IsActiveAudioProfile(p);

    /// <summary>Capture the current default devices and volumes under a name.</summary>
    public Task<AudioProfileItem?> SaveCurrentAsync(string name) => Task.Run(() =>
    {
        var p = new AudioProfileItem { Name = name.Trim() }; // ctor captures the live audio state
        return AudioProfileRepository.AddAudioProfile(p) ? p : null;
    });

    public Task<bool> ApplyAsync(AudioProfileItem p) => Task.Run(() => p.TrySetActive());

    public Task<bool> DeleteAsync(AudioProfileItem p) => Task.Run(() => AudioProfileRepository.RemoveAudioProfile(p));
}
