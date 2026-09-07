using HLab.Sys.Windows.Monitors;
using LittleBigMouse.DisplayLayout.Monitors;
using LittleBigMouse.DisplayLayout.Monitors.Extensions;
using LittleBigMouse.Platform.Windows;
using LittleBigMouse.Ui.Avalonia.Remote;
using LittleBigMouse.Zoning;

namespace Pixelscreens.Services;

/// <summary>
/// Owns the LittleBigMouse pieces: the physical layout model, its registry persistence, and the
/// Rust hook daemon. The UI talks to this and never to the upstream classes directly.
/// </summary>
public sealed class CursorEngineService : IDisposable
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private readonly ILayoutOptions _options;
    private readonly WindowsLayoutPersistence _persistence;
    private readonly SystemMonitorsService _monitors;
    private readonly WindowsLayoutFactory _factory;
    private readonly DaemonProcessManager _daemon;
    private LocalIpcClient? _ipc;

    public CursorEngineService()
    {
        _options = new PixelLayoutOptions();
        _persistence = new WindowsLayoutPersistence();
        _monitors = new SystemMonitorsService();
        _factory = new WindowsLayoutFactory(_monitors, () => new MonitorsLayout(_options), _persistence);
        _daemon = new DaemonProcessManager(new HiddenProcessHost());
    }

    public MonitorsLayout? Layout { get; private set; }
    public bool IsRunning { get; private set; }

    /// <summary>Last raw message from the daemon, for the status line.</summary>
    public event EventHandler<string>? DaemonMessage;

    /// <summary>Enumerate monitors, then overlay any saved physical placement from the registry.</summary>
    public MonitorsLayout Reload()
    {
        Layout = _factory.Create();
        return Layout;
    }

    public bool Save()
    {
        if (Layout is null) return false;
        return _persistence.Save(Layout);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var layout = Layout ?? Reload();
        _daemon.LaunchDaemon();
        _ipc ??= CreateClient();
        _ipc.Listen();

        var zones = layout.ComputeZones();
        var load = new CommandMessage(LittleBigMouseCommand.Load, zones).Serialize();
        Log("-> " + load);
        await _ipc.SendMessageAsync(load, SendTimeout, ct);
        Log("-> Run");
        await _ipc.SendMessageAsync(new CommandMessage(LittleBigMouseCommand.Run).Serialize(), SendTimeout, ct);
        IsRunning = true;
    }

    /// <summary>Push the current layout to a running daemon without restarting it.</summary>
    public async Task PushLayoutAsync(CancellationToken ct = default)
    {
        if (_ipc is null || Layout is null || !IsRunning) return;
        await _ipc.SendMessageAsync(new CommandMessage(LittleBigMouseCommand.Load, Layout.ComputeZones()).Serialize(), SendTimeout, ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_ipc is null) return;
        await _ipc.SendMessageAsync(new CommandMessage(LittleBigMouseCommand.Stop).Serialize(), SendTimeout, ct);
        IsRunning = false;
    }

    public async Task QuitAsync(CancellationToken ct = default)
    {
        if (_ipc is not null)
        {
            try
            {
                await _ipc.SendMessageAsync(new CommandMessage(LittleBigMouseCommand.Quit).Serialize(), SendTimeout, ct);
            }
            catch
            {
                // Daemon may already be gone; process manager cleans up below.
            }
        }
        IsRunning = false;
        _daemon.StopCurrentSessionDaemons();
    }

    /// <summary>
    /// Synchronous best-effort shutdown for window close: ask the daemon to quit, give it a moment,
    /// then kill anything left in this session. Never blocks on the async IPC path (UI-thread safe).
    /// </summary>
    public void Shutdown()
    {
        if (_ipc is not null && IsRunning)
        {
            var ipc = _ipc;
            _ = Task.Run(() => ipc.SendMessageAsync(new CommandMessage(LittleBigMouseCommand.Quit).Serialize(), TimeSpan.FromSeconds(1), CancellationToken.None));
            Thread.Sleep(400);
        }
        IsRunning = false;
        _daemon.StopCurrentSessionDaemons();
    }

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelScreens", "cursor-daemon.log");

    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch { /* logging is best effort */ }
    }

    private LocalIpcClient CreateClient()
    {
        var c = new LocalIpcClient();
        c.MessageReceived += (_, msg) => { Log("<- " + msg); DaemonMessage?.Invoke(this, msg); };
        c.Connected += (_, _) => Log("connected");
        c.ConnectionFailed += (_, _) => DaemonMessage?.Invoke(this, "daemon connection failed");
        return c;
    }

    public void Dispose()
    {
        _ipc?.Dispose();
        _daemon.Dispose();
    }
}
