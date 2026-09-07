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
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(BoxBrush), new SolidColorBrush(Color.Parse("#181818")));

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(BorderBrush), new SolidColorBrush(Color.Parse("#303030")));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(AccentBrush), new SolidColorBrush(Color.Parse("#FFFFFF")));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(GridBrush), new SolidColorBrush(Color.Parse("#161616")));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(TextBrush), new SolidColorBrush(Color.Parse("#F2F2F2")));

    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<LayoutCanvas, IBrush?>(nameof(MutedBrush), new SolidColorBrush(Color.Parse("#8C8C8C")));

    public static readonly StyledProperty<FontFamily?> PixelFontProperty =
        AvaloniaProperty.Register<LayoutCanvas, FontFamily?>(nameof(PixelFont));

    /// <summary>When set, only this monitor is shown, large, for detailed editing.</summary>
    public static readonly StyledProperty<MonitorBox?> FocusedProperty =
        AvaloniaProperty.Register<LayoutCanvas, MonitorBox?>(nameof(Focused));

    public MonitorBox? Focused { get => GetValue(FocusedProperty); set => SetValue(FocusedProperty, value); }

    /// <summary>Raised on double-click of a monitor box.</summary>
    public event EventHandler<MonitorBox>? MonitorOpened;

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

    /// <summary>Raised when a monitor box is clicked (or empty space, with null).</summary>
    public event EventHandler<MonitorBox?>? MonitorClicked;

    private bool _moved;

    private const double Padding = 32;
    private const double SnapMm = 5;

    private const double HandleHalf = 5;
    private enum Handle { None, NW, NE, SW, SE, N, E, S, W }
    private Handle _handle = Handle.None;
    private (double x, double y, double w, double h) _dragStartBox;
    private DateTime _lastClick;
    private MonitorBox? _lastClickBox;

    private const double SnapPx = 10;   // snap distance in canvas pixels
    private double? _snapX, _snapY;     // canvas coordinates of active guide lines

    private double _scale = 0.2;      // canvas px per mm
    private Point _origin;            // canvas point for (0mm, 0mm)
    private MonitorBox? _drag;
    private Point _dragStartPointer;
    private (double x, double y) _dragStartMm;

    static LayoutCanvas()
    {
        AffectsRender<LayoutCanvas>(MonitorsProperty, BoxBrushProperty, BorderBrushProperty, AccentBrushProperty, FocusedProperty);
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
        var all = Monitors;
        if (all is null || all.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        IEnumerable<MonitorBox> ms = Focused is not null && all.Contains(Focused) ? new[] { Focused } : all;

        double minX = ms.Min(m => m.XMm - m.BezelLeftMm), minY = ms.Min(m => m.YMm - m.BezelTopMm);
        double maxX = ms.Max(m => m.XMm + m.WidthMm + m.BezelRightMm), maxY = ms.Max(m => m.YMm + m.HeightMm + m.BezelBottomMm);
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

    /// <summary>
    /// Snap the dragged monitor's outer edges (bezels included) to other monitors' outer edges:
    /// touching (my left = their right) or aligned (my top = their top). Nearest within SnapPx wins.
    /// </summary>
    private (double x, double y) SnapToNeighbours(MonitorBox d, double x, double y)
    {
        _snapX = _snapY = null;
        var others = Monitors?.Where(m => m != d).ToList();
        if (others is null || others.Count == 0) return (x, y);

        var thr = SnapPx / _scale; // mm
        double myL = x - d.BezelLeftMm, myR = x + d.WidthMm + d.BezelRightMm;
        double myT = y - d.BezelTopMm, myB = y + d.HeightMm + d.BezelBottomMm;

        double bestDx = double.MaxValue, bestX = x, guideX = 0;
        double bestDy = double.MaxValue, bestY = y, guideY = 0;
        foreach (var o in others)
        {
            double oL = o.XMm - o.BezelLeftMm, oR = o.XMm + o.WidthMm + o.BezelRightMm;
            double oT = o.YMm - o.BezelTopMm, oB = o.YMm + o.HeightMm + o.BezelBottomMm;
            // Horizontal candidates: (my edge, target edge)
            foreach (var (mine, target) in new[] { (myL, oR), (myR, oL), (myL, oL), (myR, oR) })
            {
                var dx = target - mine;
                if (Math.Abs(dx) < Math.Abs(bestDx) && Math.Abs(dx) <= thr) { bestDx = dx; bestX = x + dx; guideX = target; }
            }
            foreach (var (mine, target) in new[] { (myT, oB), (myB, oT), (myT, oT), (myB, oB) })
            {
                var dy = target - mine;
                if (Math.Abs(dy) < Math.Abs(bestDy) && Math.Abs(dy) <= thr) { bestDy = dy; bestY = y + dy; guideY = target; }
            }
        }
        if (bestDx != double.MaxValue) { x = bestX; _snapX = _origin.X + guideX * _scale; }
        if (bestDy != double.MaxValue) { y = bestY; _snapY = _origin.Y + guideY * _scale; }
        return (x, y);
    }

    private IEnumerable<(Point pt, Handle h)> Handles(MonitorBox m, Rect r)
    {
        var bl = m.BezelLeftMm * _scale; var bt = m.BezelTopMm * _scale;
        var br = m.BezelRightMm * _scale; var bb = m.BezelBottomMm * _scale;
        yield return (r.TopLeft, Handle.NW);
        yield return (r.TopRight, Handle.NE);
        yield return (r.BottomLeft, Handle.SW);
        yield return (r.BottomRight, Handle.SE);
        yield return (new Point(r.Center.X, r.Y - bt), Handle.N);
        yield return (new Point(r.Right + br, r.Center.Y), Handle.E);
        yield return (new Point(r.Center.X, r.Bottom + bb), Handle.S);
        yield return (new Point(r.X - bl, r.Center.Y), Handle.W);
    }

    private Handle HitHandle(MonitorBox m, Point p)
    {
        var r = ToCanvas(m);
        foreach (var (pt, h) in Handles(m, r))
            if (Math.Abs(pt.X - p.X) <= HandleHalf + 2 && Math.Abs(pt.Y - p.Y) <= HandleHalf + 2) return h;
        return Handle.None;
    }

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
            if (Focused is not null && m != Focused) continue;
            var r = ToCanvas(m);
            r = new Rect(Math.Round(r.X) + 0.5, Math.Round(r.Y) + 0.5, Math.Round(r.Width), Math.Round(r.Height));
            // Bezel frame (outer edge of the physical monitor), drawn dimmer behind the panel.
            var bl = m.BezelLeftMm * _scale; var bt = m.BezelTopMm * _scale;
            var br = m.BezelRightMm * _scale; var bb = m.BezelBottomMm * _scale;
            if (bl + bt + br + bb > 0)
            {
                var outer = new Rect(r.X - Math.Round(bl), r.Y - Math.Round(bt), r.Width + Math.Round(bl + br), r.Height + Math.Round(bt + bb));
                ctx.FillRectangle(GridBrush ?? Brushes.Transparent, outer);
                ctx.DrawRectangle(border, outer);
            }
            ctx.FillRectangle(BoxBrush ?? Brushes.Transparent, r);
            ctx.DrawRectangle(m == _drag || m.IsSelected ? accent : border, r);

            // Corner ticks give it a retro "selection box" feel without rounded corners.
            const double tick = 6;
            var tp = new Pen(AccentBrush, 1);
            ctx.DrawLine(tp, r.TopLeft, r.TopLeft + new Vector(tick, 0));
            ctx.DrawLine(tp, r.TopLeft, r.TopLeft + new Vector(0, tick));
            ctx.DrawLine(tp, r.BottomRight, r.BottomRight - new Vector(tick, 0));
            ctx.DrawLine(tp, r.BottomRight, r.BottomRight - new Vector(0, tick));

            if (r.Width < 40 || r.Height < 24) continue;
            var compact = r.Width < 170 || r.Height < 110;

            var title = new FormattedText(m.Label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(font), 8, TextBrush) { MaxTextWidth = r.Width - 16 };
            ctx.DrawText(title, new Point(r.X + 8, r.Y + 8));
            if (compact) continue;

            var sub = new FormattedText(m.Detail, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, MutedBrush) { MaxTextWidth = r.Width - 16 };
            ctx.DrawText(sub, new Point(r.X + 8, r.Y + 8 + title.Height + 6));

            var size = new FormattedText(m.SizeText, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, MutedBrush);
            ctx.DrawText(size, new Point(r.X + 8, r.Bottom - size.Height - 8));
        }

        // Snap guides while dragging.
        if (_drag is not null && (_snapX is not null || _snapY is not null))
        {
            var gp = new Pen(AccentBrush, 1, new DashStyle(new double[] { 4, 3 }, 0));
            if (_snapX is double gx) ctx.DrawLine(gp, new Point(Math.Round(gx) + 0.5, 0), new Point(Math.Round(gx) + 0.5, Bounds.Height));
            if (_snapY is double gy) ctx.DrawLine(gp, new Point(0, Math.Round(gy) + 0.5), new Point(Bounds.Width, Math.Round(gy) + 0.5));
        }

        // Handles on the selected monitor: corners resize the panel, edges set that side's bezel.
        var sel = ms.FirstOrDefault(m => m.IsSelected);
        if (sel is not null && (Focused is null || sel == Focused))
        {
            var r = ToCanvas(sel);
            var fill = AccentBrush ?? Brushes.White;
            foreach (var (pt, _) in Handles(sel, r))
                ctx.FillRectangle(fill, new Rect(pt.X - HandleHalf, pt.Y - HandleHalf, HandleHalf * 2, HandleHalf * 2));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var ms = Monitors;
        if (ms is null) return;
        var p = e.GetPosition(this);
        var hit = false;

        // Resize / bezel handles on the selected monitor take priority.
        var sel = ms.FirstOrDefault(m => m.IsSelected);
        if (sel is not null && (Focused is null || sel == Focused))
        {
            var h = HitHandle(sel, p);
            if (h != Handle.None)
            {
                _drag = sel;
                _handle = h;
                _moved = false;
                _dragStartPointer = p;
                _dragStartBox = (sel.XMm, sel.YMm, sel.WidthMm, sel.HeightMm);
                e.Pointer.Capture(this);
                return;
            }
        }

        // Topmost hit wins: iterate in reverse so later items (drawn on top) take precedence.
        for (var i = ms.Count - 1; i >= 0; i--)
        {
            if (Focused is not null && ms[i] != Focused) continue;
            if (!ToCanvas(ms[i]).Contains(p)) continue;
            var now = DateTime.UtcNow;
            if (_lastClickBox == ms[i] && (now - _lastClick).TotalMilliseconds < 400)
            {
                _lastClick = DateTime.MinValue;
                MonitorOpened?.Invoke(this, ms[i]);
                return;
            }
            _lastClick = now;
            _lastClickBox = ms[i];
            _drag = ms[i];
            _handle = Handle.None;
            _moved = false;
            _dragStartPointer = p;
            _dragStartMm = (_drag.XMm, _drag.YMm);
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            InvalidateVisual();
            hit = true;
            break;
        }
        if (!hit) MonitorClicked?.Invoke(this, null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is null)
        {
            var selHover = Monitors?.FirstOrDefault(m => m.IsSelected);
            var h = selHover is null ? Handle.None : HitHandle(selHover, e.GetPosition(this));
            Cursor = h switch
            {
                Handle.NW or Handle.SE => new Cursor(StandardCursorType.TopLeftCorner),
                Handle.NE or Handle.SW => new Cursor(StandardCursorType.TopRightCorner),
                Handle.N or Handle.S => new Cursor(StandardCursorType.SizeNorthSouth),
                Handle.E or Handle.W => new Cursor(StandardCursorType.SizeWestEast),
                _ => Cursor.Default,
            };
            return;
        }
        var p = e.GetPosition(this);
        var dxMm = (p.X - _dragStartPointer.X) / _scale;
        var dyMm = (p.Y - _dragStartPointer.Y) / _scale;
        if (Math.Abs(p.X - _dragStartPointer.X) + Math.Abs(p.Y - _dragStartPointer.Y) > 3) _moved = true;
        if (!_moved) return;

        if (_handle != Handle.None)
        {
            var (x0, y0, w0, h0) = _dragStartBox;
            var pmm = new Point((p.X - _origin.X) / _scale, (p.Y - _origin.Y) / _scale); // pointer in mm
            switch (_handle)
            {
                case Handle.SE: _drag.ResizeToWidth(pmm.X - x0); break;
                case Handle.NE: _drag.ResizeToWidth(pmm.X - x0); _drag.YMm = y0 + h0 - _drag.HeightMm; break;
                case Handle.SW: _drag.ResizeToWidth(x0 + w0 - pmm.X); _drag.XMm = x0 + w0 - _drag.WidthMm; break;
                case Handle.NW: _drag.ResizeToWidth(x0 + w0 - pmm.X); _drag.XMm = x0 + w0 - _drag.WidthMm; _drag.YMm = y0 + h0 - _drag.HeightMm; break;
                case Handle.N: _drag.BezelTopMm = Math.Clamp(Math.Round(y0 - pmm.Y, 1), 0, 150); break;
                case Handle.S: _drag.BezelBottomMm = Math.Clamp(Math.Round(pmm.Y - (y0 + h0), 1), 0, 150); break;
                case Handle.W: _drag.BezelLeftMm = Math.Clamp(Math.Round(x0 - pmm.X, 1), 0, 150); break;
                case Handle.E: _drag.BezelRightMm = Math.Clamp(Math.Round(pmm.X - (x0 + w0), 1), 0, 150); break;
            }
            return;
        }

        var nx = _dragStartMm.x + dxMm;
        var ny = _dragStartMm.y + dyMm;
        (nx, ny) = SnapToNeighbours(_drag, nx, ny);
        _drag.XMm = Math.Round(nx * 10) / 10;
        _drag.YMm = Math.Round(ny * 10) / 10;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_drag is null) Cursor = Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is null) return;
        e.Pointer.Capture(null);
        var clicked = _drag;
        var wasHandle = _handle != Handle.None;
        _drag = null;
        _snapX = _snapY = null;
        _handle = Handle.None;
        Cursor = Cursor.Default;
        InvalidateVisual();
        if (!wasHandle) MonitorClicked?.Invoke(this, clicked);
        if (_moved) LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
