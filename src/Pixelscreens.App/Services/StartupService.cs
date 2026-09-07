using System.Diagnostics;

namespace Pixelscreens.Services;

/// <summary>
/// Start-with-Windows via a Task Scheduler logon task with highest privileges. A plain Run-key
/// entry would raise a UAC prompt at every logon because the app manifest requires admin.
/// </summary>
public static class StartupService
{
    private const string TaskName = "PixelScreens";

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Pixelscreens.exe");

    public static bool IsEnabled()
    {
        var (code, _) = Run($"/query /tn \"{TaskName}\"");
        return code == 0;
    }

    public static (bool ok, string message) Enable()
    {
        var tr = $"\\\"{ExePath}\\\" --minimized";
        var (code, output) = Run($"/create /f /tn \"{TaskName}\" /sc onlogon /rl highest /it /tr \"{tr}\"");
        return (code == 0, code == 0 ? "PixelScreens will start when you sign in." : output.Trim());
    }

    public static (bool ok, string message) Disable()
    {
        var (code, output) = Run($"/delete /f /tn \"{TaskName}\"");
        return (code == 0, code == 0 ? "Start with Windows turned off." : output.Trim());
    }

    private static (int code, string output) Run(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
