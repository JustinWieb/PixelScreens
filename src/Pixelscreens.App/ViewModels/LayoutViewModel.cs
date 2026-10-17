using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LittleBigMouse.DisplayLayout.Monitors;
using LittleBigMouse.DisplayLayout.Monitors.Extensions;
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
            if (ev == "Running") { IsRunning = true; _lastRunningAt = DateTime.UtcNow; }
            if (ev is "Stopped" or "Dead") IsRunning = false;
            // The engine stops itself on some Windows display/setting changes. If the user wants it on, bring it back.
            if (_wantRunning && ev is "Stopped" or "Dead" or "SettingChanged" or "Resumed") ScheduleRecover(ev);
        });
    }

    /// <summary>What the user asked for: on unless they pressed Disable. Survives engine self-stops.</summary>
    private bool _wantRunning;
    private DateTime _lastRunningAt;
    private CancellationTokenSource? _recover;

    private void ScheduleRecover(string why)
    {
        var at = DateTime.UtcNow;
        _recover?.Cancel();
        var cts = _recover = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            // Let Windows finish rearranging (and ignore the Stopped→Running blip of a normal reload).
            try { await Task.Delay(2500, cts.Token); } catch (OperationCanceledException) { return; }
            await Dispatcher.UIThread.InvokeAsync(() => RecoverAsync(why, at, cts.Token));
        });
    }

    private async Task RecoverAsync(string why, DateTime at, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 5 && _wantRunning && !ct.IsCancellationRequested; attempt++)
        {
            // Still (or again) running: nothing to fix. This also skips the Stopped→Running blip of a normal reload.
            if (IsRunning) return;
            try
            {
                await RefreshAsync();          // re-read monitors, they may have changed
                await _engine.StartAsync(ct);  // load zones for what's plugged in now and run
                IsRunning = true;
                Status = "enabled · restarted after a display change";
                return;
            }
            catch (Exception ex)
            {
                Status = $"restart attempt {attempt} failed: {Describe(ex)}";
                try { await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    public ObservableCollection<MonitorBox> Monitors { get; } = new();

    /// <summary>Raised whenever the desk layout is (re)loaded or saved, so other views can mirror it.</summary>
    public event Action? DeskChanged;
    public IEnumerable<DeskScreen> Desk => Monitors.Select(b => new DeskScreen(b.Pnp, b.XMm, b.YMm, b.WidthMm, b.HeightMm));

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
        LoadScale(box);
    }

    // Windows scaling for the selected monitor.
    public System.Collections.ObjectModel.ObservableCollection<string> ScaleOptions { get; } = new();
    [ObservableProperty] private string? _selectedScale;
    [ObservableProperty] private bool _hasScale;
    private bool _loadingScale;

    private static string ScaleText(uint pct, uint recommended) => pct == recommended ? $"{pct}% (recommended)" : $"{pct}%";

    private void LoadScale(MonitorBox? box)
    {
        _loadingScale = true;
        try
        {
            ScaleOptions.Clear();
            var info = box is null || box.GdiName.Length == 0 ? null : ScalingService.Get(box.GdiName);
            HasScale = info is not null;
            if (info is null) { SelectedScale = null; return; }
            foreach (var pct in info.Allowed) ScaleOptions.Add(ScaleText(pct, info.Recommended));
            SelectedScale = ScaleText(info.Current, info.Recommended);
        }
        catch
        {
            HasScale = false;
        }
        finally
        {
            _loadingScale = false;
        }
    }

    partial void OnSelectedScaleChanged(string? value)
    {
        if (_loadingScale || value is null || Selected is not { } box) return;
        if (!uint.TryParse(value.Split('%')[0], out var pct)) return;
        var ok = ScalingService.Set(box.GdiName, pct);
        Status = ok ? $"{box.Label} scaling set to {pct}%" : $"Windows refused {pct}% scaling for {box.Label}";
        if (!ok) LoadScale(box);
    }

    /// <summary>
    /// Re-place every monitor from the Windows arrangement (who sits left of whom, who is above), using real sizes.
    /// Use when the physical layout drifted from what Windows has, e.g. after a profile switch or a new monitor.
    /// </summary>
    [RelayCommand]
    private async Task MatchWindowsAsync()
    {
        if (_engine.Layout is not { } layout) return;
        Busy = true;
        try
        {
            await Task.Run(() =>
            {
                layout.SetLocationsFromSystemConfiguration(placeAll: true);
                _engine.Save();
            });
        }
        catch (Exception ex)
        {
            Status = $"match failed: {Describe(ex)}";
            return;
        }
        finally
        {
            Busy = false;
        }
        await RefreshAsync();
        Status = "physical layout rebuilt from the Windows arrangement" + (IsRunning ? " · live" : "");
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (Busy) return; // an apply and the Windows display event can both ask at once
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
                    GdiName = src?.DisplayName ?? "",
                    Pnp = (m.Model.PnpCode ?? "").ToUpperInvariant(),
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
            var keep = Selected?.Id;
            Select(Monitors.FirstOrDefault(b => b.Id == keep) ?? Monitors.FirstOrDefault(b => b.IsPrimary) ?? Monitors.FirstOrDefault());
            Dirty = false;
            EngineOk = true;
            IsRunning = _engine.IsRunning;
            // The monitor set may have changed under a running hook; give it the zones for what is plugged in now.
            if (IsRunning) await _engine.PushLayoutAsync();
            Status = $"{Monitors.Count} monitor(s) found" + (IsRunning ? " · enabled" : " · disabled");
            DeskChanged?.Invoke();
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
            if (ok) DeskChanged?.Invoke();
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
            _wantRunning = true;
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
            _wantRunning = false;
            _recover?.Cancel();
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
