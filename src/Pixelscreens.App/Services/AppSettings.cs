using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixelscreens.Services;

/// <summary>PixelScreens' own settings. Stored as JSON under %LOCALAPPDATA%\PixelScreens.</summary>
public sealed class AppSettings
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelScreens");
    public static string FilePath => Path.Combine(Dir, "settings.json");

    // Startup and window
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool EnableCursorOnStart { get; set; } = true;
    public bool UnitsInches { get; set; } = true;

    // Cursor engine (mirrors LittleBigMouse's ILayoutOptions, the parts a user cares about)
    public string Algorithm { get; set; } = "Cross";
    public bool LoopX { get; set; }
    public bool LoopY { get; set; }
    public double MaxTravelDistance { get; set; } = 200;
    public bool AllowDiscontinuity { get; set; }
    public bool AllowOverlaps { get; set; }
    public bool AdjustPointer { get; set; } = true;
    public bool AdjustSpeed { get; set; }
    public bool FreelookEnabled { get; set; } = true;
    public string RescueShortcut { get; set; } = "Ctrl+Alt+Shift+M";
    public List<string> ExcludedApps { get; set; } = new();

    /// <summary>Global hotkeys. Key: action id ("show", "cursor.toggle", "profile:{uuid}", "audio:{uuid}"). Value: gesture like "Ctrl+Alt+D1".</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
        }
        catch
        {
            // Corrupt settings fall back to defaults; the next Save overwrites them.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
