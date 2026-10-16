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

    /// <summary>
    /// Which monitor each of a profile's screens is. DisplayMagician names a screen by its NVIDIA display id
    /// (or Windows target id), so look that id up in the profile's display identifiers.
    /// </summary>
    public static Dictionary<string, MonitorRef> ScreenMonitors(ProfileItem profile)
    {
        var map = new Dictionary<string, MonitorRef>();
        var names = Of(profile).ToDictionary(r => r.Pnp, r => r.Name);
        string NameFor(string pnp) => names.TryGetValue(pnp, out var n) ? n : "";

        // NVIDIA|gpu|bus|n|connector|EDID maker|EDID product (decimal)|serial|display id
        foreach (var id in profile.ProfileDisplayIdentifiers ?? [])
        {
            var f = id.Split('|');
            if (f.Length < 9 || f[0] != "NVIDIA" || !int.TryParse(f[6], out var product)) continue;
            var pnp = (f[5] + product.ToString("X4")).ToUpperInvariant();
            map.TryAdd(f[8], new MonitorRef(pnp, NameFor(pnp)));
        }
        // WINAPI identifiers carry the target id inside the device path as UID<target>.
        foreach (var id in profile.WindowsDisplayConfig.DisplayIdentifiers ?? [])
        {
            var pnp = PnpRx.Match(id ?? "");
            var uid = Regex.Match(id ?? "", @"UID(\d+)#");
            if (pnp.Success && uid.Success)
                map.TryAdd(uid.Groups[1].Value, new MonitorRef(pnp.Groups[1].Value.ToUpperInvariant(), NameFor(pnp.Groups[1].Value.ToUpperInvariant())));
        }
        return map;
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
