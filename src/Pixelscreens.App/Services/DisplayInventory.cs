using System.Text.RegularExpressions;
using DisplayMagicianShared;
using DisplayMagicianShared.Windows;

namespace Pixelscreens.Services;

/// <summary>A physical monitor, identified by its EDID PnP code (e.g. GBT3204) so the port it is plugged into doesn't matter.</summary>
public sealed record MonitorRef(string Pnp, string Name)
{
    public override string ToString() => Name.Length > 0 ? Name : Pnp;
}

/// <summary>
/// Which monitors are plugged in and which are switched on right now, read from the Windows CCD API.
/// DisplayMagician's own profile matching compares whole driver configs and its identifier format changed
/// between versions, so a profile saved by an older build never reads as active. We compare monitors instead.
/// </summary>
public sealed class DisplayInventory
{
    private static readonly Regex PnpRx = new(@"DISPLAY#([^#]+)#", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<MonitorRef> Active { get; private init; } = [];
    public IReadOnlyList<MonitorRef> Connected { get; private init; } = [];

    public static DisplayInventory Capture()
    {
        var win = WinLibrary.GetLibrary();
        var active = Parse(win.GetCurrentDisplayIdentifiers());
        var connected = Parse(win.GetAllConnectedDisplayIdentifiers());
        // An active monitor is connected by definition, even if the ALL_PATHS query missed it.
        connected = connected.Concat(Except(active, connected)).ToList();
        return new DisplayInventory { Active = active, Connected = connected };
    }

    /// <summary>The monitors a saved profile turns on.</summary>
    public static IReadOnlyList<MonitorRef> Of(ProfileItem profile) =>
        Parse(profile.WindowsDisplayConfig.DisplayIdentifiers ?? []);

    public static IReadOnlyList<MonitorRef> Parse(IEnumerable<string> identifiers)
    {
        var list = new List<MonitorRef>();
        foreach (var id in identifiers)
        {
            var m = PnpRx.Match(id ?? "");
            if (!m.Success) continue;
            var name = id!.Split('|').LastOrDefault()?.Trim() ?? "";
            if (name == "#") name = "";
            list.Add(new MonitorRef(m.Groups[1].Value.ToUpperInvariant(), name));
        }
        // QDC_ALL_PATHS lists a monitor once per possible source, so collapse duplicates by PnP code.
        return list.GroupBy(r => r.Pnp).Select(g => g.First()).OrderBy(r => r.ToString()).ToList();
    }

    /// <summary>Items in <paramref name="a"/> whose PnP code is not in <paramref name="b"/>.</summary>
    public static List<MonitorRef> Except(IEnumerable<MonitorRef> a, IEnumerable<MonitorRef> b)
    {
        var codes = b.Select(r => r.Pnp).ToHashSet();
        return a.Where(r => !codes.Contains(r.Pnp)).ToList();
    }

    public static bool SameSet(IEnumerable<MonitorRef> a, IEnumerable<MonitorRef> b) =>
        a.Select(r => r.Pnp).ToHashSet().SetEquals(b.Select(r => r.Pnp));
}
