using DisplayMagicianShared.Windows;

namespace Pixelscreens.Services;

/// <summary>
/// Windows display scaling (100%, 125%, 150%...) per monitor, the same setting as Settings > Display > Scale.
/// Scaling belongs to a display source, which we find by its GDI name (\\.\DISPLAY1).
/// </summary>
public static class ScalingService
{
    /// <summary>The steps Windows offers, in order. Which ones a monitor allows depends on its size and resolution.</summary>
    public static readonly uint[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    public sealed record Info(uint Current, uint Recommended, IReadOnlyList<uint> Allowed);

    public static Info? Get(string gdiName)
    {
        if (Source(gdiName) is not { } src) return null;
        var dpi = WinLibrary.GetLibrary().GetDPISettings(src.AdapterId, src.SourceId, src.TargetId);
        if (dpi.Current == 0) return null;
        var allowed = Steps.Where(s => s >= dpi.Minimum && s <= dpi.Maximum).ToList();
        return new Info(dpi.Current, dpi.Recommended, allowed.Count > 0 ? allowed : [dpi.Current]);
    }

    public static bool Set(string gdiName, uint percent)
    {
        if (Source(gdiName) is not { } src) return false;
        return WinLibrary.GetLibrary().SetDPISettings(src.AdapterId, src.SourceId, src.TargetId, new DPIScalingInfo { Current = percent });
    }

    private static DISPLAY_SOURCE? Source(string gdiName)
    {
        var sources = WinLibrary.GetLibrary().GetActiveConfig().DisplaySources;
        return sources is not null && sources.TryGetValue(gdiName, out var list) && list.Count > 0 ? list[0] : null;
    }
}
