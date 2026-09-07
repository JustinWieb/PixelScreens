using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LittleBigMouse.DisplayLayout.Monitors;
using Pixelscreens.Services;

namespace Pixelscreens.ViewModels;

public sealed partial class LayoutViewModel : ObservableObject
{
    private readonly CursorEngineService _engine;
    private readonly Dictionary<string, PhysicalMonitor> _byId = new();

    public LayoutViewModel(CursorEngineService engine)
    {
        _engine = engine;
        _engine.DaemonMessage += (_, msg) => Dispatcher.UIThread.Post(() =>
        {
            LastDaemonMessage = Trim(msg);
            var ev = System.Text.RegularExpressions.Regex.Match(msg, "<Event>([^<]+)</Event>").Groups[1].Value;
            if (ev.Length > 0 && ev != "FocusChanged") Status = ev switch
            {
                "Running" => "cursor fix on",
                "Stopped" => "cursor fix off",
                "Loaded" => "layout loaded by cursor engine",
                "LoadFailed" => "cursor engine rejected the layout (see log)",
                "Dead" => "cursor engine connection lost",
                _ => $"cursor engine: {ev}",
            };
            if (ev == "Running") IsRunning = true;
            if (ev is "Stopped" or "Dead") IsRunning = false;
        });
    }

    public ObservableCollection<MonitorBox> Monitors { get; } = new();

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _engineOk;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _dirty;
    [ObservableProperty] private string _lastDaemonMessage = "";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Busy = true;
        try
        {
            var layout = await Task.Run(() => _engine.Reload());
            Monitors.Clear();
            _byId.Clear();
            foreach (var m in layout.PhysicalMonitors)
            {
                var src = m.ActiveSource?.Source;
                var proj = m.DepthProjection;
                var box = new MonitorBox
                {
                    Id = m.Id,
                    Label = Pick(m.Model.PnpDeviceName, src?.DisplayName, m.Model.PnpCode, m.Id).ToUpperInvariant(),
                    Detail = src is null ? "" : $"{src.InPixel.Width:0}x{src.InPixel.Height:0} · {src.EffectiveDpi.X:0} dpi · {src.DisplayName?.Replace(@"\\.\", "")}",
                    IsPrimary = src?.Primary ?? false,
                    XMm = proj.X,
                    YMm = proj.Y,
                    WidthMm = proj.Width,
                    HeightMm = proj.Height,
                    PixelWidth = (int)(src?.InPixel.Width ?? 0),
                    PixelHeight = (int)(src?.InPixel.Height ?? 0),
                };
                box.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(MonitorBox.XMm) or nameof(MonitorBox.YMm)) PushPosition(box);
                };
                Monitors.Add(box);
                _byId[m.Id] = m;
            }
            Dirty = false;
            EngineOk = true;
            IsRunning = _engine.IsRunning;
            Status = $"{Monitors.Count} monitor(s) found" + (IsRunning ? " · cursor fix on" : " · cursor fix off");
        }
        catch (Exception ex)
        {
            EngineOk = false;
            Status = $"cursor engine failed: {Describe(ex)}";
        }
        finally
        {
            Busy = false;
        }
    }

    private void PushPosition(MonitorBox box)
    {
        if (!_byId.TryGetValue(box.Id, out var m)) return;
        m.DepthProjection.X = box.XMm;
        m.DepthProjection.Y = box.YMm;
        Dirty = true;
    }

    /// <summary>Called by the canvas when a drag ends: persist and, if the hook is live, push the new zones.</summary>
    public async Task CommitDragAsync()
    {
        if (!Dirty) return;
        await SaveAsync();
        if (IsRunning)
        {
            try { await _engine.PushLayoutAsync(); }
            catch (Exception ex) { Status = $"push failed: {ex.Message}"; }
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var ok = await Task.Run(() => _engine.Save());
            Dirty = !ok;
            Status = ok ? "layout saved" : "layout save failed";
        }
        catch (Exception ex)
        {
            Status = $"layout save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        Busy = true;
        Status = "turning cursor fix on...";
        try
        {
            await _engine.StartAsync();
            IsRunning = true;
            Status = "cursor fix on";
        }
        catch (Exception ex)
        {
            Status = $"start failed: {Describe(ex)}";
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        Busy = true;
        try
        {
            await _engine.StopAsync();
            IsRunning = false;
            Status = "cursor fix off";
        }
        catch (Exception ex)
        {
            Status = $"stop failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private static string Pick(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "";

    private static string Describe(Exception ex)
    {
        var root = ex;
        while (root.InnerException is not null) root = root.InnerException;
        return root == ex ? $"{ex.GetType().Name}: {ex.Message}" : $"{ex.GetType().Name}: {ex.Message} → {root.GetType().Name}: {root.Message}";
    }

    private static string Trim(string s) => s.Length > 160 ? s[..160] + "…" : s;
}
