using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Pixelscreens.Controls;

/// <summary>
/// A tiny two-frame pixel sprite that walks along its own width and wraps.
/// Drawn as filled rectangles from a bitmap string so it stays crisp at any DPI.
/// </summary>
public sealed class PixelSprite : Control
{
    // 8x8 frames. '#' body, 'o' eye, '.' empty. A little CRT-style ghost.
    private static readonly string[] Frame0 =
    {
        "..####..",
        ".######.",
        "##o##o##",
        "########",
        "########",
        "########",
        "#.####.#",
        "#..##..#",
    };

    private static readonly string[] Frame1 =
    {
        "..####..",
        ".######.",
        "##o##o##",
        "########",
        "########",
        "########",
        ".#.##.#.",
        "..#..#..",
    };

    public static readonly StyledProperty<double> PixelSizeProperty =
        AvaloniaProperty.Register<PixelSprite, double>(nameof(PixelSize), 3);

    public static readonly StyledProperty<IBrush?> BodyBrushProperty =
        AvaloniaProperty.Register<PixelSprite, IBrush?>(nameof(BodyBrush), Brushes.MediumSlateBlue);

    public static readonly StyledProperty<IBrush?> EyeBrushProperty =
        AvaloniaProperty.Register<PixelSprite, IBrush?>(nameof(EyeBrush), Brushes.Black);

    public static readonly StyledProperty<double> SpeedProperty =
        AvaloniaProperty.Register<PixelSprite, double>(nameof(Speed), 24);

    public double PixelSize { get => GetValue(PixelSizeProperty); set => SetValue(PixelSizeProperty, value); }
    public IBrush? BodyBrush { get => GetValue(BodyBrushProperty); set => SetValue(BodyBrushProperty, value); }
    public IBrush? EyeBrush { get => GetValue(EyeBrushProperty); set => SetValue(EyeBrushProperty, value); }

    /// <summary>Pixels per second along the track.</summary>
    public double Speed { get => GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }

    private readonly DispatcherTimer _timer;
    private double _x;
    private int _frame;
    private int _tick;
    private DateTime _last = DateTime.UtcNow;

    public PixelSprite()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, OnTick);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _last = DateTime.UtcNow;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _last).TotalSeconds;
        _last = now;

        _x += Speed * dt;
        var spriteWidth = 8 * PixelSize;
        if (_x > Bounds.Width) _x = -spriteWidth;

        if (++_tick % 5 == 0) _frame ^= 1;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = 8 * PixelSize;
        var w = double.IsInfinity(availableSize.Width) ? 8 * PixelSize : availableSize.Width;
        return new Size(w, h);
    }

    public override void Render(DrawingContext context)
    {
        var frame = _frame == 0 ? Frame0 : Frame1;
        var px = PixelSize;
        var ox = Math.Round(_x / px) * px; // snap to the pixel grid so it never blurs

        for (var row = 0; row < frame.Length; row++)
        {
            var line = frame[row];
            for (var col = 0; col < line.Length; col++)
            {
                var c = line[col];
                if (c == '.') continue;
                var brush = c == 'o' ? EyeBrush : BodyBrush;
                if (brush is null) continue;
                context.FillRectangle(brush, new Rect(ox + col * px, row * px, px, px));
            }
        }
    }
}
