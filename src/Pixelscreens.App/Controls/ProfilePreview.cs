using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Pixelscreens.ViewModels;

namespace Pixelscreens.Controls;

/// <summary>
/// Thumbnail of a display profile: each screen as a box at its pixel position. Screens whose monitor is
/// plugged in are tinted green, missing ones red; the primary screen gets a heavier outline.
/// </summary>
public sealed class ProfilePreview : Control
{
    public static readonly StyledProperty<IReadOnlyList<ScreenRect>?> ScreensProperty =
        AvaloniaProperty.Register<ProfilePreview, IReadOnlyList<ScreenRect>?>(nameof(Screens));

    public static readonly StyledProperty<bool> IsUsableProperty =
        AvaloniaProperty.Register<ProfilePreview, bool>(nameof(IsUsable), true);

    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<ProfilePreview, bool>(nameof(ShowLabels));

    public IReadOnlyList<ScreenRect>? Screens { get => GetValue(ScreensProperty); set => SetValue(ScreensProperty, value); }
    public bool IsUsable { get => GetValue(IsUsableProperty); set => SetValue(IsUsableProperty, value); }
    /// <summary>Write each monitor's name and orientation inside its box (for the larger current-setup preview).</summary>
    public bool ShowLabels { get => GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }

    private static readonly IBrush Back = new SolidColorBrush(Color.Parse("#0A0A0A"));
    private static readonly IBrush Frame = new SolidColorBrush(Color.Parse("#303030"));

    private static readonly IBrush Fill = new SolidColorBrush(Color.Parse("#181818"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#5A5A5A"));
    private static readonly IBrush FillOk = new SolidColorBrush(Color.Parse("#16271C"));
    private static readonly IBrush LineOk = new SolidColorBrush(Color.Parse("#8FE0A6"));
    private static readonly IBrush FillBad = new SolidColorBrush(Color.Parse("#2C1717"));
    private static readonly IBrush LineBad = new SolidColorBrush(Color.Parse("#FF8C8C"));

    static ProfilePreview()
    {
        AffectsRender<ProfilePreview>(ScreensProperty, IsUsableProperty, ShowLabelsProperty);
    }

    protected override Size MeasureOverride(Size availableSize) => new(150, 84);

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width; var h = Bounds.Height;
        ctx.FillRectangle(Back, new Rect(0, 0, w, h));
        ctx.DrawRectangle(new Pen(Frame, 1), new Rect(0.5, 0.5, w - 1, h - 1));

        var ss = Screens;
        if (ss is null || ss.Count == 0) return;

        var pad = ShowLabels ? 14 : 8;
        double minX = ss.Min(s => s.X), minY = ss.Min(s => s.Y);
        double maxX = ss.Max(s => s.X + s.W), maxY = ss.Max(s => s.Y + s.H);
        var scale = Math.Min((w - 2 * pad) / Math.Max(1, maxX - minX), (h - 2 * pad) / Math.Max(1, maxY - minY));
        var ox = (w - (maxX - minX) * scale) / 2 - minX * scale;
        var oy = (h - (maxY - minY) * scale) / 2 - minY * scale;

        foreach (var s in ss.OrderBy(s => s.IsPrimary))
        {
            var r = new Rect(Math.Round(ox + s.X * scale) + 0.5, Math.Round(oy + s.Y * scale) + 0.5,
                Math.Max(2, Math.Round(s.W * scale) - 2), Math.Max(2, Math.Round(s.H * scale) - 2));
            var (fill, line) = s.State switch
            {
                ScreenState.Present => (FillOk, LineOk),
                ScreenState.Missing => (FillBad, LineBad),
                _ => (Fill, Line),
            };
            ctx.FillRectangle(fill, r);
            ctx.DrawRectangle(new Pen(line, s.IsPrimary ? 2 : 1), r);
            if (ShowLabels) DrawLabel(ctx, r, s, line);
        }
    }

    private static void DrawLabel(DrawingContext ctx, Rect r, ScreenRect s, IBrush brush)
    {
        if (r.Width < 40 || r.Height < 22) return;
        var text = s.Label + "\n" + s.Orientation;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, brush)
        {
            MaxTextWidth = r.Width - 8,
            MaxTextHeight = r.Height - 6,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        using (ctx.PushClip(r))
            ctx.DrawText(ft, new Point(r.X + 4, r.Y + 3));
    }
}
