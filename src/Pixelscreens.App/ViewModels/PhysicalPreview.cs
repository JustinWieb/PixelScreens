namespace Pixelscreens.ViewModels;

/// <summary>A monitor's picture area on the desk, in millimetres, as placed on the Layout tab.</summary>
public sealed record DeskScreen(string Pnp, double X, double Y, double W, double H);

/// <summary>
/// Turns a profile's pixel arrangement into a desk-true one, so previews match the Layout tab:
/// a 4K 32" and a 1080p 24" are drawn at their real sizes, not their pixel counts.
/// </summary>
public static class PhysicalPreview
{
    private const double MmPerPx96 = 25.4 / 96;

    /// <param name="useLive">The arrangement is the one on screen: copy positions straight from the Layout tab.</param>
    public static IReadOnlyList<ScreenRect> Apply(IReadOnlyList<ScreenRect> screens, IReadOnlyDictionary<string, DeskScreen> live,
        IReadOnlyDictionary<string, double[]> sizes, bool useLive)
    {
        if (screens.Count == 0) return screens;

        if (useLive && screens.All(s => s.Pnp.Length > 0 && live.ContainsKey(s.Pnp)))
        {
            return screens.Select(s =>
            {
                var d = live[s.Pnp];
                return s with { PX = d.X, PY = d.Y, PW = d.W, PH = d.H, HasPhys = true };
            }).ToList();
        }

        // Otherwise lay them out from Windows' adjacency, using each monitor's real size.
        var size = screens.ToDictionary(s => s, s => SizeOf(s, live, sizes));
        var placed = new Dictionary<ScreenRect, (double X, double Y)>();
        var first = screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
        placed[first] = (0, 0);

        bool progress = true;
        while (placed.Count < screens.Count && progress)
        {
            progress = false;
            foreach (var s in screens.Where(s => !placed.ContainsKey(s)))
            {
                foreach (var (p, pos) in placed.ToList())
                {
                    var (pw, ph) = size[p];
                    var (sw, sh) = size[s];
                    double sy(ScreenRect a) => pos.Y + (a.Y - p.Y) * ph / Math.Max(1, p.H);
                    double sx(ScreenRect a) => pos.X + (a.X - p.X) * pw / Math.Max(1, p.W);
                    var overlapY = s.Y < p.Y + p.H && p.Y < s.Y + s.H;
                    var overlapX = s.X < p.X + p.W && p.X < s.X + s.W;
                    (double, double)? at =
                        s.X + s.W == p.X && overlapY ? (pos.X - sw, sy(s)) :
                        s.X == p.X + p.W && overlapY ? (pos.X + pw, sy(s)) :
                        s.Y + s.H == p.Y && overlapX ? (sx(s), pos.Y - sh) :
                        s.Y == p.Y + p.H && overlapX ? (sx(s), pos.Y + ph) :
                        null;
                    if (at is null) continue;
                    placed[s] = at.Value;
                    progress = true;
                    break;
                }
            }
        }
        // Screens that touch nothing: fall back to the primary's scale.
        foreach (var s in screens.Where(s => !placed.ContainsKey(s)))
            placed[s] = (s.X * MmPerPx96, s.Y * MmPerPx96);

        return screens.Select(s =>
        {
            var (w, h) = size[s];
            var (x, y) = placed[s];
            return s with { PX = x, PY = y, PW = w, PH = h, HasPhys = true };
        }).ToList();
    }

    private static (double W, double H) SizeOf(ScreenRect s, IReadOnlyDictionary<string, DeskScreen> live, IReadOnlyDictionary<string, double[]> sizes)
    {
        double w, h;
        if (live.TryGetValue(s.Pnp, out var d)) (w, h) = (d.W, d.H);
        else if (sizes.TryGetValue(s.Pnp, out var mm) && mm.Length == 2) (w, h) = (mm[0], mm[1]);
        else return (s.W * MmPerPx96, s.H * MmPerPx96);
        // Saved size may be landscape while this profile has the monitor in portrait (or the reverse).
        if (w > h != s.W > s.H) (w, h) = (h, w);
        return (w, h);
    }
}
