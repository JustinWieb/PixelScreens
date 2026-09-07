using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Pixelscreens.ViewModels;

namespace Pixelscreens.Controls;

/// <summary>Thumbnail of a display profile: each screen as a box at its pixel position, primary highlighted.</summary>
public sealed class ProfilePreview : Control
{
    public static readonly StyledProperty<IReadOnlyList<ScreenRect>?> ScreensProperty =
        AvaloniaProperty.Register<ProfilePreview, IReadOnlyList<ScreenRect>?>(nameof(Screens));

    public static readonly StyledProperty<bool> IsUsableProperty =
        AvaloniaProperty.Register<ProfilePreview, bool>(nameof(IsUsable), true);

    public IReadOnlyList<ScreenRect>? Screens { get => GetValue(ScreensProperty); set => SetValue(ScreensProperty, value); }
    public bool IsUsable { get => GetValue(IsUsableProperty); set => SetValue(IsUsableProperty, value); }

    private static readonly IBrush Fill = new SolidColorBrush(Color.Parse("#181818"));
    private static readonly IBrush FillPrimary = new SolidColorBrush(Color.Parse("#2A2A2A"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#303030"));
    private static readonly IBrush LinePrimary = new SolidColorBrush(Color.Parse("#F2F2F2"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#FF5C5C"));

    static ProfilePreview()
    {
        AffectsRender<ProfilePreview>(ScreensProperty, IsUsableProperty);
    }

    protected override Size MeasureOverride(Size availableSize) => new(150, 84);

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width; var h = Bounds.Height;
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#0A0A0A")), new Rect(0, 0, w, h));
        ctx.DrawRectangle(new Pen(Line, 1), new Rect(0.5, 0.5, w - 1, h - 1));

        var ss = Screens;
        if (ss is null || ss.Count == 0) return;

        double minX = ss.Min(s => s.X), minY = ss.Min(s => s.Y);
        double maxX = ss.Max(s => s.X + s.W), maxY = ss.Max(s => s.Y + s.H);
        var scale = Math.Min((w - 12) / Math.Max(1, maxX - minX), (h - 12) / Math.Max(1, maxY - minY));
        var ox = (w - (maxX - minX) * scale) / 2 - minX * scale;
        var oy = (h - (maxY - minY) * scale) / 2 - minY * scale;

        foreach (var s in ss.OrderBy(s => s.IsPrimary))
        {
            var r = new Rect(Math.Round(ox + s.X * scale) + 0.5, Math.Round(oy + s.Y * scale) + 0.5,
                Math.Max(2, Math.Round(s.W * scale) - 1), Math.Max(2, Math.Round(s.H * scale) - 1));
            ctx.FillRectangle(s.IsPrimary ? FillPrimary : Fill, r);
            ctx.DrawRectangle(new Pen(s.IsPrimary ? LinePrimary : Line, 1), r);
        }

        if (!IsUsable)
        {
            // Small red corner tick: "some display is missing".
            ctx.FillRectangle(Warn, new Rect(w - 10, 4, 6, 6));
        }
    }
}
