using LittleBigMouse.DisplayLayout.Monitors;

namespace Pixelscreens.Services;

/// <summary>
/// Cursor-engine options fed to LittleBigMouse's model. Values come from <see cref="AppSettings"/>;
/// call <see cref="Apply"/> after the user changes them.
/// </summary>
public sealed class PixelLayoutOptions : ILayoutOptions.Design
{
    public PixelLayoutOptions(AppSettings settings)
    {
        Apply(settings);
    }

    public void Apply(AppSettings s)
    {
        Algorithm = s.Algorithm;
        LoopX = s.LoopX;
        LoopY = s.LoopY;
        MaxTravelDistance = s.MaxTravelDistance;
        AllowDiscontinuity = s.AllowDiscontinuity;
        AllowOverlaps = s.AllowOverlaps;
        AdjustPointer = s.AdjustPointer;
        AdjustSpeed = s.AdjustSpeed;
        FreelookEnabled = s.FreelookEnabled;
        RescueShortcut = s.RescueShortcut;
        LoadAtStartup = false; // PixelScreens owns startup, not LittleBigMouse's task
        ExcludedList.Clear();
        foreach (var e in s.ExcludedApps.Where(x => !string.IsNullOrWhiteSpace(x))) ExcludedList.Add(e.Trim());
    }
}
