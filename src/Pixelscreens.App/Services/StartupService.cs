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
        // Written as task XML because schtasks /create flags can't lift the defaults that broke "every time":
        // a 72 hour run limit (Windows killed the app, and the cursor engine with it, after three days)
        // and no start on battery.
        var user = System.Security.SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        var exe = System.Security.SecurityElement.Escape(ExePath);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{exe}</Command><Arguments>--minimized</Arguments></Exec>
              </Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), "pixelscreens-task.xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try
        {
            var (code, output) = Run($"/create /f /tn \"{TaskName}\" /xml \"{file}\"");
            return (code == 0, code == 0 ? "PixelScreens will start when you sign in." : output.Trim());
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Re-create a task made by an older build so it gets the settings above. Only touches the task when it
    /// launches this same exe, so running a test build elsewhere never repoints sign-in startup at it.
    /// </summary>
    public static void Repair()
    {
        var (code, xml) = Run($"/query /tn \"{TaskName}\" /xml");
        if (code != 0) return;
        var launchesThis = xml.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        var current = xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
        if (launchesThis && !current) Enable();
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
