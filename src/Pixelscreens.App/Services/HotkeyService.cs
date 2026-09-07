using System.Runtime.InteropServices;
using Avalonia.Input;

namespace Pixelscreens.Services;

/// <summary>
/// System-wide hotkeys via Win32 RegisterHotKey on a dedicated message-loop thread.
/// Gestures are strings like "Ctrl+Alt+D1" or "Ctrl+Shift+F5" (Avalonia key names).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_APP_REBIND = 0x8001;
    private const int WM_QUIT = 0x0012;

    private readonly Dictionary<int, string> _idToAction = new();
    private readonly Dictionary<string, string> _pending = new(); // action -> gesture
    private readonly object _gate = new();
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _stopping;

    /// <summary>Raised on the hotkey thread; marshal to the UI thread before touching controls.</summary>
    public event Action<string>? Triggered;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "PixelScreens hotkeys" };
        _thread.Start();
    }

    /// <summary>Replace every binding. Unknown or invalid gestures are skipped.</summary>
    public void Rebind(IReadOnlyDictionary<string, string> bindings)
    {
        lock (_gate)
        {
            _pending.Clear();
            foreach (var (action, gesture) in bindings)
                if (!string.IsNullOrWhiteSpace(gesture)) _pending[action] = gesture;
        }
        if (_threadId != 0) PostThreadMessage(_threadId, WM_APP_REBIND, IntPtr.Zero, IntPtr.Zero);
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        // Force creation of the thread's message queue before anyone posts to it.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        ApplyPending();

        while (!_stopping && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY)
            {
                var id = (int)msg.wParam;
                string? action;
                lock (_gate) _idToAction.TryGetValue(id, out action);
                if (action is not null) Triggered?.Invoke(action);
            }
            else if (msg.message == WM_APP_REBIND)
            {
                ApplyPending();
            }
        }
        UnregisterAll();
    }

    private void ApplyPending()
    {
        UnregisterAll();
        lock (_gate)
        {
            var id = 1;
            foreach (var (action, gesture) in _pending)
            {
                if (!TryParse(gesture, out var mods, out var vk)) continue;
                if (RegisterHotKey(IntPtr.Zero, id, mods | MOD_NOREPEAT, vk))
                {
                    _idToAction[id] = action;
                    id++;
                }
            }
        }
    }

    private void UnregisterAll()
    {
        lock (_gate)
        {
            foreach (var id in _idToAction.Keys) UnregisterHotKey(IntPtr.Zero, id);
            _idToAction.Clear();
        }
    }

    public static string Format(KeyModifiers mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Human display: "Ctrl+Alt+D1" -> "Ctrl+Alt+1".</summary>
    public static string Pretty(string gesture) =>
        string.Join("+", gesture.Split('+').Select(p => p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1]) ? p[1..] : p));

    public static bool TryParse(string gesture, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": case "meta": mods |= MOD_WIN; break;
                default: return false;
            }
        }
        if (!Enum.TryParse<Key>(parts[^1], true, out var key)) return false;
        vk = ToVirtualKey(key);
        return vk != 0;
    }

    private static uint ToVirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return (uint)('A' + (key - Key.A));
        if (key >= Key.D0 && key <= Key.D9) return (uint)('0' + (key - Key.D0));
        if (key >= Key.F1 && key <= Key.F24) return 0x70u + (uint)(key - Key.F1);
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return 0x60u + (uint)(key - Key.NumPad0);
        return key switch
        {
            Key.Space => 0x20, Key.Enter => 0x0D, Key.Tab => 0x09, Key.Escape => 0x1B,
            Key.Home => 0x24, Key.End => 0x23, Key.PageUp => 0x21, Key.PageDown => 0x22,
            Key.Insert => 0x2D, Key.Delete => 0x2E, Key.Pause => 0x13, Key.Scroll => 0x91,
            Key.Left => 0x25, Key.Up => 0x26, Key.Right => 0x27, Key.Down => 0x28,
            Key.OemMinus => 0xBD, Key.OemPlus => 0xBB, Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB, Key.OemCloseBrackets => 0xDD, Key.OemPipe => 0xDC,
            Key.OemSemicolon => 0xBA, Key.OemQuotes => 0xDE, Key.OemComma => 0xBC,
            Key.OemPeriod => 0xBE, Key.OemQuestion => 0xBF,
            _ => 0,
        };
    }

    public void Dispose()
    {
        _stopping = true;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
