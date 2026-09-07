using System.Diagnostics;
using LittleBigMouse.Ui.Avalonia.Remote;

namespace Pixelscreens.Services;

/// <summary>
/// Same as LittleBigMouse's SystemProcessHost, but always launches the daemon without a console
/// window. Upstream shows one in Debug builds; users of PixelScreens should never see it.
/// </summary>
public sealed class HiddenProcessHost : IProcessHost
{
    private readonly SystemProcessHost _inner = new();

    public int CurrentSessionId => _inner.CurrentSessionId;

    public IEnumerable<IDaemonProcess> Enumerate(IEnumerable<string> processNames) => _inner.Enumerate(processNames);

    public IDaemonProcess Launch(string path)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            },
        };
        process.Start();
        return new Owned(process);
    }

    private sealed class Owned(Process process) : IDaemonProcess
    {
        public bool HasExited => process.HasExited;
        public int SessionId => process.SessionId;
        public int Id => process.Id;
        public string ProcessName => process.ProcessName;

        public bool TryStop()
        {
            try
            {
                if (!process.HasExited) process.Kill();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose() => process.Dispose();
    }
}
