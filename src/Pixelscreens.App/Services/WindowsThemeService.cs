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

    // Windows stores accent colours as ABGR. E8E8EE (Pixelscreens text white) -> 0xFFEEE8E8.
    private const uint AccentAbgr = 0xFFEEE8E8;

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

    public static (bool dark, bool prevalence) Read()
    {
        using var p = Registry.CurrentUser.OpenSubKey(Personalize);
        using var d = Registry.CurrentUser.OpenSubKey(Dwm);
        var dark = (int?)p?.GetValue("AppsUseLightTheme") == 0 && (int?)p?.GetValue("SystemUsesLightTheme") == 0;
        var prev = (int?)d?.GetValue("ColorPrevalence") == 1;
        return (dark, prev);
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
