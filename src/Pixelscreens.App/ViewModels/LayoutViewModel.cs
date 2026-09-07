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

    private readonly AppSettings _settings;

    public LayoutViewModel(CursorEngineService engine, AppSettings settings)
    {
        _engine = engine;
        _settings = settings;
        _useInches = settings.UnitsInches;
        _engine.DaemonMessage += (_, msg) => Dispatcher.UIThread.Post(() =>
        {
            LastDaemonMessage = Trim(msg);
            var ev = System.Text.RegularExpressions.Regex.Match(msg, "<Event>([^<]+)</Event>").Groups[1].Value;
            if (ev.Length > 0 && ev != "FocusChanged") Status = ev switch
            {
                "Running" => "enabled",
                "Stopped" => "disabled",
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
    [ObservableProperty] private MonitorBox? _selected;
    [ObservableProperty] private MonitorBox? _focused;
    [ObservableProperty] private bool _useInches = true;

    public bool IsFocused => Focused is not null;
    public string UnitLabel => UseInches ? "in" : "mm";

    partial void OnFocusedChanged(MonitorBox? value)
    {
        OnPropertyChanged(nameof(IsFocused));
        if (value is not null) Select(value);
    }

    partial void OnUseInchesChanged(bool value)
    {
        foreach (var m in Monitors) m.UseInches = value;
        OnPropertyChanged(nameof(UnitLabel));
        _settings.UnitsInches = value;
        _settings.Save();
    }

    [RelayCommand] private void OpenSelected() { if (Selected is not null) Focused = Selected; }
    [RelayCommand] private void ShowAll() => Focused = null;
    public void Open(MonitorBox box) => Focused = box;

    public bool HasSelection => Selected is not null;
    partial void OnSelectedChanged(MonitorBox? value) => OnPropertyChanged(nameof(HasSelection));

    public void Select(MonitorBox? box)
    {
        foreach (var m in Monitors) m.IsSelected = m == box;
        Selected = box;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Busy = true;
        try
        {
            var layout = await Task.Run(() => _engine.Reload());
            Monitors.Clear();
            _byId.Clear();
            Focused = null;
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
                    BezelTopMm = Math.Round(m.PhysicalRotated.TopBorder, 1),
                    BezelRightMm = Math.Round(m.PhysicalRotated.RightBorder, 1),
                    BezelBottomMm = Math.Round(m.PhysicalRotated.BottomBorder, 1),
                    BezelLeftMm = Math.Round(m.PhysicalRotated.LeftBorder, 1),
                };
                box.UseInches = UseInches;
                box.SeedDiagonal();
                box.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(MonitorBox.XMm) or nameof(MonitorBox.YMm)) PushPosition(box);
                };
                box.Edited += (_, _) => PushSize(box);
                Monitors.Add(box);
                _byId[m.Id] = m;
            }
            Select(Monitors.FirstOrDefault(b => b.IsPrimary) ?? Monitors.FirstOrDefault());
            Dirty = false;
            EngineOk = true;
            IsRunning = _engine.IsRunning;
            Status = $"{Monitors.Count} monitor(s) found" + (IsRunning ? " · enabled" : " · disabled");
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
        ScheduleAutosave();
    }

    private CancellationTokenSource? _autosave;

    /// <summary>Save 800 ms after the last edit, so typing a number doesn't hammer the registry.</summary>
    private void ScheduleAutosave()
    {
        _autosave?.Cancel();
        var cts = _autosave = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(800, cts.Token); } catch (OperationCanceledException) { return; }
            await Dispatcher.UIThread.InvokeAsync(async () => { if (Dirty) await SaveAsync(); });
        });
    }

    private void PushSize(MonitorBox box)
    {
        if (!_byId.TryGetValue(box.Id, out var m)) return;
        var phys = m.PhysicalRotated;
        if (Math.Abs(phys.Width - box.WidthMm) > 0.05) phys.Width = box.WidthMm;
        if (Math.Abs(phys.Height - box.HeightMm) > 0.05) phys.Height = box.HeightMm;
        if (Math.Abs(phys.TopBorder - box.BezelTopMm) > 0.05 || Math.Abs(phys.RightBorder - box.BezelRightMm) > 0.05 ||
            Math.Abs(phys.BottomBorder - box.BezelBottomMm) > 0.05 || Math.Abs(phys.LeftBorder - box.BezelLeftMm) > 0.05)
        {
            m.BordersCustomized = true;
            phys.TopBorder = box.BezelTopMm;
            phys.RightBorder = box.BezelRightMm;
            phys.BottomBorder = box.BezelBottomMm;
            phys.LeftBorder = box.BezelLeftMm;
        }
        Dirty = true;
        ScheduleAutosave();
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
            Status = ok ? $"saved {DateTime.Now:HH:mm:ss}" + (IsRunning ? " · live" : "") : "layout save failed";
            if (ok && IsRunning) await _engine.PushLayoutAsync();
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
        Status = "enabling...";
        try
        {
            await _engine.StartAsync();
            IsRunning = true;
            Status = "enabled";
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
            Status = "disabled";
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
