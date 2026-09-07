using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pixelscreens.Services;

/// <summary>
/// Pushes the Pixelscreens look onto Windows itself: dark mode for apps and shell, a near-white
/// accent so title bars, the Start menu, and taskbar highlights match, and no accent on the
/// taskbar background. Only registry writes under HKCU; nothing here needs admin.
/// </summary>
public static class WindowsThemeService
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";
    private const string Accent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    // Windows stores accent colours as ABGR. F2F2F2 (Pixelscreens text white) -> 0xFFF2F2F2.
    private const uint AccentAbgr = 0xFFF2F2F2;

    public static void ApplyMonochromeDark()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Personalize)!)
        {
            k.SetValue("AppsUseLightTheme", 0, RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", 0, RegistryValueKind.DWord);
            k.SetValue("EnableTransparency", 0, RegistryValueKind.DWord);
        }

        using (var k = Registry.CurrentUser.CreateSubKey(Dwm)!)
        {
            k.SetValue("AccentColor", unchecked((int)AccentAbgr), RegistryValueKind.DWord);
            k.SetValue("ColorizationColor", unchecked((int)AccentAbgr), RegistryValueKind.DWord);
            k.SetValue("ColorizationAfterglow", unchecked((int)AccentAbgr), RegistryValueKind.DWord);
            k.SetValue("ColorPrevalence", 1, RegistryValueKind.DWord); // accent on title bars and window borders
        }

        using (var k = Registry.CurrentUser.CreateSubKey(Accent)!)
        {
            k.SetValue("AccentColorMenu", unchecked((int)AccentAbgr), RegistryValueKind.DWord);
        }

        using (var k = Registry.CurrentUser.CreateSubKey(Personalize)!)
        {
            k.SetValue("ColorPrevalence", 0, RegistryValueKind.DWord); // keep Start/taskbar black, not accent-tinted
        }

        Broadcast("ImmersiveColorSet");
    }

    /// <summary>Set the Windows accent colour from a hex string like "#7C7CFF". Does not touch dark mode or title bars.</summary>
    public static void SetAccent(string hex)
    {
        var c = System.Drawing.ColorTranslator.FromHtml(hex);
        var abgr = 0xFF000000u | ((uint)c.B << 16) | ((uint)c.G << 8) | c.R;
        using (var k = Registry.CurrentUser.CreateSubKey(Dwm)!)
        {
            k.SetValue("AccentColor", unchecked((int)abgr), RegistryValueKind.DWord);
            k.SetValue("ColorizationColor", unchecked((int)abgr), RegistryValueKind.DWord);
            k.SetValue("ColorizationAfterglow", unchecked((int)abgr), RegistryValueKind.DWord);
        }
        using (var k = Registry.CurrentUser.CreateSubKey(Accent)!)
        {
            k.SetValue("AccentColorMenu", unchecked((int)abgr), RegistryValueKind.DWord);
        }
        Broadcast("ImmersiveColorSet");
    }

    public static string ReadAccentHex()
    {
        using var d = Registry.CurrentUser.OpenSubKey(Dwm);
        if (d?.GetValue("AccentColor") is int v)
        {
            var u = unchecked((uint)v);
            return $"#{u & 0xFF:X2}{(u >> 8) & 0xFF:X2}{(u >> 16) & 0xFF:X2}";
        }
        return "#0078D4";
    }

    public static void SetDarkMode(bool dark)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Personalize)!;
        k.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        k.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        Broadcast("ImmersiveColorSet");
    }

    /// <summary>Accent on window title bars and borders (the thing that draws a coloured line around windows).</summary>
    public static void SetAccentOnTitleBars(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Dwm)!;
        k.SetValue("ColorPrevalence", on ? 1 : 0, RegistryValueKind.DWord);
        Broadcast("ImmersiveColorSet");
    }

    public static void SetTransparency(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Personalize)!;
        k.SetValue("EnableTransparency", on ? 1 : 0, RegistryValueKind.DWord);
        Broadcast("ImmersiveColorSet");
    }

    public static bool ReadTransparency()
    {
        using var p = Registry.CurrentUser.OpenSubKey(Personalize);
        return (int?)p?.GetValue("EnableTransparency") != 0;
    }

    private const string ExplorerPolicy = @"Software\Policies\Microsoft\Windows\Explorer";
    public static bool ReadWebSearch()
    {
        using var k = Registry.CurrentUser.OpenSubKey(ExplorerPolicy);
        return (int?)k?.GetValue("DisableSearchBoxSuggestions") != 1;
    }
    public static void SetWebSearch(bool on) => Set(ExplorerPolicy, "DisableSearchBoxSuggestions", on ? 0 : 1, "SearchSettings");

    public static (bool dark, bool prevalence) Read()
    {
        using var p = Registry.CurrentUser.OpenSubKey(Personalize);
        using var d = Registry.CurrentUser.OpenSubKey(Dwm);
        var dark = (int?)p?.GetValue("AppsUseLightTheme") == 0 && (int?)p?.GetValue("SystemUsesLightTheme") == 0;
        var prev = (int?)d?.GetValue("ColorPrevalence") == 1;
        return (dark, prev);
    }

    // ---- Taskbar and search tweaks (all HKCU, most apply live; some need Explorer restarted) ----
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string SearchSettings = @"Software\Microsoft\Windows\CurrentVersion\SearchSettings";
    private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";

    public sealed record DesktopState(bool SearchHighlights, int SearchBoxMode, bool Widgets, bool TaskView, bool Chat, bool Copilot, bool CenterTaskbar, bool AccentOnTaskbar);

    public static DesktopState ReadDesktop()
    {
        using var adv = Registry.CurrentUser.OpenSubKey(Advanced);
        using var ss = Registry.CurrentUser.OpenSubKey(SearchSettings);
        using var se = Registry.CurrentUser.OpenSubKey(Search);
        using var p = Registry.CurrentUser.OpenSubKey(Personalize);
        int Get(RegistryKey? k, string name, int def) => k?.GetValue(name) is int i ? i : def;
        return new DesktopState(
            SearchHighlights: Get(ss, "IsDynamicSearchBoxEnabled", 1) == 1,
            SearchBoxMode: Get(se, "SearchboxTaskbarMode", 2),
            Widgets: Get(adv, "TaskbarDa", 1) == 1,
            TaskView: Get(adv, "ShowTaskViewButton", 1) == 1,
            Chat: Get(adv, "TaskbarMn", 1) == 1,
            Copilot: Get(adv, "ShowCopilotButton", 1) == 1,
            CenterTaskbar: Get(adv, "TaskbarAl", 1) == 1,
            AccentOnTaskbar: Get(p, "ColorPrevalence", 0) == 1);
    }

    public static void SetSearchHighlights(bool on) => Set(SearchSettings, "IsDynamicSearchBoxEnabled", on ? 1 : 0, "SearchSettings");
    /// <summary>0 hidden, 1 icon only, 2 search box, 3 icon and label.</summary>
    public static void SetSearchBoxMode(int mode) => Set(Search, "SearchboxTaskbarMode", mode, "TraySettings");
    public static void SetWidgets(bool on) => Set(Advanced, "TaskbarDa", on ? 1 : 0, "TraySettings");
    public static void SetTaskView(bool on) => Set(Advanced, "ShowTaskViewButton", on ? 1 : 0, "TraySettings");
    public static void SetChat(bool on) => Set(Advanced, "TaskbarMn", on ? 1 : 0, "TraySettings");
    public static void SetCopilot(bool on) => Set(Advanced, "ShowCopilotButton", on ? 1 : 0, "TraySettings");
    public static void SetCenterTaskbar(bool center) => Set(Advanced, "TaskbarAl", center ? 1 : 0, "TraySettings");
    public static void SetAccentOnTaskbar(bool on) => Set(Personalize, "ColorPrevalence", on ? 1 : 0, "ImmersiveColorSet");

    private static void Set(string key, string name, int value, string broadcastSetting)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(key)!) k.SetValue(name, value, RegistryValueKind.DWord);
        Broadcast(broadcastSetting);
    }

    /// <summary>Restart Explorer so taskbar changes that Windows only reads at startup take effect.</summary>
    public static void RestartExplorer()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); } catch { /* may already be gone */ }
        }
        Thread.Sleep(800);
        if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }

    private static void Broadcast(string setting)
    {
        const int HWND_BROADCAST = 0xFFFF;
        const uint WM_SETTINGCHANGE = 0x001A;
        SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, setting, 0x0002 /*SMTO_ABORTIFHUNG*/, 2000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
