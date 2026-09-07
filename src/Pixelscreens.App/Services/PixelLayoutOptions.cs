using LittleBigMouse.DisplayLayout.Monitors;

namespace Pixelscreens.Services;

/// <summary>
/// Cursor-engine options. Starts from LittleBigMouse's design defaults; persistence of these
/// values into Pixelscreens' own settings comes later.
/// </summary>
public sealed class PixelLayoutOptions : ILayoutOptions.Design
{
    public PixelLayoutOptions()
    {
        AdjustPointer = true;   // scale the pointer size across DPI boundaries
        Algorithm = "Cross";    // straight-line crossing is the least surprising default
        LoadAtStartup = false;
    }
}
