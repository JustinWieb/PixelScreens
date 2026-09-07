using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Pixelscreens.ViewModels;

namespace Pixelscreens.Controls;

/// <summary>
/// Draws monitors at their physical size and position (millimetres), scaled to fit.
/// Boxes are draggable; dragging writes back to <see cref="MonitorBox.XMm"/> / <see cref="MonitorBox.YMm"/>.
/// Pixel-grid look: hairline borders, no anti-aliased curves, a dotted mm grid behind.
/// </summary>
public sealed class LayoutCanvas : Control
{
    public static readonly StyledProperty<ObservableCollection<MonitorBox>?> MonitorsProperty =
        AvaloniaProperty.Register<LayoutCanvas, ObservableCollection<MonitorBox>?>(nameof(Monitors));

    public static readonly StyledProperty<IBrush?> BoxBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(BoxBrush), new SolidColorBrush(Color.Parse("#1B1B22")));

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(BorderBrush), new SolidColorBrush(Color.Parse("#34343F")));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(AccentBrush), new SolidColorBrush(Color.Parse("#7C7CFF")));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(GridBrush), new SolidColorBrush(Color.Parse("#1A1A21")));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(TextBrush), new SolidColorBrush(Color.Parse("#E8E8EE")));

    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(MutedBrush), new SolidColorBrush(Color.Parse("#8B8B98")));

    public static readonly StyledProperty<FontFamily?> PixelFontProperty =
        AvaloniaProperty.Register<LayoutCanvas, FontFamily?>(nameof(PixelFont));

    public ObservableCollection<MonitorBox>? Monitors { get => GetValue(MonitorsProperty); set => SetValue(MonitorsProperty, value); }
    public IBrush? BoxBrush { get => GetValue(BoxBrushProperty); set => SetValue(BoxBrushProperty, value); }
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public IBrush? MutedBrush { get => GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public FontFamily? PixelFont { get => GetValue(PixelFontProperty); set => SetValue(PixelFontProperty, value); }

    /// <summary>Raised after a drag ends, so the owner can persist or push the layout to the engine.</summary>
    public event EventHandler? LayoutChanged;

    private const double Padding = 32;
    private const double SnapMm = 5;

    private double _scale = 0.2;      // canvas px per mm
    private Point _origin;            // canvas point for (0mm, 0mm)
    private MonitorBox? _drag;
    private Point _dragStartPointer;
    private (double x, double y) _dragStartMm;

    static LayoutCanvas()
    {
        AffectsRender<LayoutCanvas>(MonitorsProperty, BoxBrushProperty, BorderBrushProperty, AccentBrushProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MonitorsProperty)
        {
            if (change.OldValue is ObservableCollection<MonitorBox> old)
            {
                old.CollectionChanged -= OnMonitorsChanged;
                foreach (var m in old) m.PropertyChanged -= OnMonitorChanged;
            }
            if (change.NewValue is ObservableCollection<MonitorBox> now)
            {
                now.CollectionChanged += OnMonitorsChanged;
                foreach (var m in now) m.PropertyChanged += OnMonitorChanged;
            }
            InvalidateVisual();
        }
    }

    private void OnMonitorsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (MonitorBox m in e.OldItems) m.PropertyChanged -= OnMonitorChanged;
        if (e.NewItems is not null) foreach (MonitorBox m in e.NewItems) m.PropertyChanged += OnMonitorChanged;
        InvalidateVisual();
    }

    private void OnMonitorChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private void Fit()
    {
        var ms = Monitors;
        if (ms is null || ms.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        double minX = ms.Min(m => m.XMm), minY = ms.Min(m => m.YMm);
        double maxX = ms.Max(m => m.XMm + m.WidthMm), maxY = ms.Max(m => m.YMm + m.HeightMm);
        var wMm = Math.Max(1, maxX - minX);
        var hMm = Math.Max(1, maxY - minY);

        var sx = (Bounds.Width - 2 * Padding) / wMm;
        var sy = (Bounds.Height - 2 * Padding) / hMm;
        _scale = Math.Max(0.01, Math.Min(sx, sy));

        var contentW = wMm * _scale;
        var contentH = hMm * _scale;
        _origin = new Point(
            (Bounds.Width - contentW) / 2 - minX * _scale,
            (Bounds.Height - contentH) / 2 - minY * _scale);
    }

    private Rect ToCanvas(MonitorBox m) => new(
        _origin.X + m.XMm * _scale,
        _origin.Y + m.YMm * _scale,
        m.WidthMm * _scale,
        m.HeightMm * _scale);

    public override void Render(DrawingContext ctx)
    {
        // Re-fit unless mid-drag, so the view never jumps under the pointer.
        if (_drag is null) Fit();

        // Background grid every 100mm.
        if (GridBrush is not null && _scale > 0)
        {
            var step = 100 * _scale;
            var pen = new Pen(GridBrush, 1);
            var startX = _origin.X % step;
            var startY = _origin.Y % step;
            for (var x = startX; x < Bounds.Width; x += step)
                ctx.DrawLine(pen, new Point(Math.Round(x) + 0.5, 0), new Point(Math.Round(x) + 0.5, Bounds.Height));
            for (var y = startY; y < Bounds.Height; y += step)
                ctx.DrawLine(pen, new Point(0, Math.Round(y) + 0.5), new Point(Bounds.Width, Math.Round(y) + 0.5));
        }

        var ms = Monitors;
        if (ms is null) return;

        var border = new Pen(BorderBrush, 1);
        var accent = new Pen(AccentBrush, 2);
        var font = PixelFont ?? FontFamily.Default;

        foreach (var m in ms)
        {
            var r = ToCanvas(m);
            r = new Rect(Math.Round(r.X) + 0.5, Math.Round(r.Y) + 0.5, Math.Round(r.Width), Math.Round(r.Height));
            ctx.FillRectangle(BoxBrush ?? Brushes.Transparent, r);
            ctx.DrawRectangle(m == _drag || m.IsPrimary ? accent : border, r);

            // Corner ticks give it a retro "selection box" feel without rounded corners.
            const double tick = 6;
            var tp = new Pen(AccentBrush, 1);
            ctx.DrawLine(tp, r.TopLeft, r.TopLeft + new Vector(tick, 0));
            ctx.DrawLine(tp, r.TopLeft, r.TopLeft + new Vector(0, tick));
            ctx.DrawLine(tp, r.BottomRight, r.BottomRight - new Vector(tick, 0));
            ctx.DrawLine(tp, r.BottomRight, r.BottomRight - new Vector(0, tick));

            if (r.Width < 40 || r.Height < 24) continue;

            var title = new FormattedText(m.Label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(font), 8, TextBrush) { MaxTextWidth = r.Width - 16 };
            ctx.DrawText(title, new Point(r.X + 8, r.Y + 8));

            var sub = new FormattedText(m.Detail, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, MutedBrush) { MaxTextWidth = r.Width - 16 };
            ctx.DrawText(sub, new Point(r.X + 8, r.Y + 8 + title.Height + 6));

            var size = new FormattedText($"{m.WidthMm:0} x {m.HeightMm:0} mm", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, MutedBrush);
            ctx.DrawText(size, new Point(r.X + 8, r.Bottom - size.Height - 8));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var ms = Monitors;
        if (ms is null) return;
        var p = e.GetPosition(this);
        // Topmost hit wins: iterate in reverse so later items (drawn on top) take precedence.
        for (var i = ms.Count - 1; i >= 0; i--)
        {
            if (!ToCanvas(ms[i]).Contains(p)) continue;
            _drag = ms[i];
            _dragStartPointer = p;
            _dragStartMm = (_drag.XMm, _drag.YMm);
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            InvalidateVisual();
            break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is null) return;
        var p = e.GetPosition(this);
        var dxMm = (p.X - _dragStartPointer.X) / _scale;
        var dyMm = (p.Y - _dragStartPointer.Y) / _scale;
        _drag.XMm = Math.Round((_dragStartMm.x + dxMm) / SnapMm) * SnapMm;
        _drag.YMm = Math.Round((_dragStartMm.y + dyMm) / SnapMm) * SnapMm;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is null) return;
        e.Pointer.Capture(null);
        _drag = null;
        Cursor = Cursor.Default;
        InvalidateVisual();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
