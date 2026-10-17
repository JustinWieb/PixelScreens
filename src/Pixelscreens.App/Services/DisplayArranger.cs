using System.Runtime.InteropServices;

namespace Pixelscreens.Services;

/// <summary>
/// Puts a chosen set of monitors on screen at given positions and resolutions through the Windows CCD API
/// (the same call Settings > Display makes). Used to rebuild a profile onto different hardware: the profile
/// says where screens go, the user says which live monitor fills each spot.
/// </summary>
public static class DisplayArranger
{
    /// <summary>One screen to show. Position is in desktop pixels; the primary must sit at 0,0 (we shift to make it so).</summary>
    public sealed record Placement(string Pnp, int X, int Y, int Width, int Height, bool Primary, int RotationDegrees);

    public static (bool ok, string message) Apply(IReadOnlyList<Placement> placements)
    {
        if (placements.Count == 0) return (false, "nothing to show");
        if (placements.Select(p => p.Pnp).Distinct().Count() != placements.Count) return (false, "the same monitor is used twice");

        var err = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out var pathCount, out var modeCount);
        if (err != 0) return (false, $"GetDisplayConfigBufferSizes failed ({err})");
        var paths = new PATH_INFO[pathCount];
        var modes = new MODE_INFO[modeCount];
        err = QueryDisplayConfig(QDC_ALL_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (err != 0) return (false, $"QueryDisplayConfig failed ({err})");

        // Which monitor sits behind each (adapter, target): read its device path, which carries the PnP code.
        var pnpOf = new Dictionary<(long, uint), string>();
        foreach (var p in paths.Take(pathCount))
        {
            if (p.targetInfo.targetAvailable == 0) continue;
            var key = (p.targetInfo.adapterId.Value, p.targetInfo.id);
            if (pnpOf.ContainsKey(key)) continue;
            var name = new TARGET_DEVICE_NAME();
            name.header.type = DEVICE_INFO_GET_TARGET_NAME;
            name.header.size = (uint)Marshal.SizeOf<TARGET_DEVICE_NAME>();
            name.header.adapterId = p.targetInfo.adapterId;
            name.header.id = p.targetInfo.id;
            if (DisplayConfigGetDeviceInfo(ref name) != 0) continue;
            var m = System.Text.RegularExpressions.Regex.Match(name.monitorDevicePath ?? "", @"DISPLAY#([^#]+)#", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) pnpOf[key] = m.Groups[1].Value.ToUpperInvariant();
        }

        var primary = placements.FirstOrDefault(p => p.Primary) ?? placements[0];
        var chosen = new List<PATH_INFO>();
        var newModes = new List<MODE_INFO>();
        var usedSources = new HashSet<(long, uint)>();
        var usedTargets = new HashSet<(long, uint)>();

        foreach (var want in placements)
        {
            // Every source x target pairing is listed; prefer the one already active so Windows changes as little as possible.
            var candidates = paths.Take(pathCount)
                .Where(p => p.targetInfo.targetAvailable != 0
                            && pnpOf.TryGetValue((p.targetInfo.adapterId.Value, p.targetInfo.id), out var pnp) && pnp == want.Pnp
                            && !usedSources.Contains((p.sourceInfo.adapterId.Value, p.sourceInfo.id))
                            && !usedTargets.Contains((p.targetInfo.adapterId.Value, p.targetInfo.id)))
                .OrderByDescending(p => (p.flags & PATH_ACTIVE) != 0)
                .ToList();
            if (candidates.Count == 0) return (false, $"{want.Pnp} isn't plugged in or has no free output");

            var path = candidates[0];
            usedSources.Add((path.sourceInfo.adapterId.Value, path.sourceInfo.id));
            usedTargets.Add((path.targetInfo.adapterId.Value, path.targetInfo.id));

            var mode = new MODE_INFO
            {
                infoType = MODE_INFO_TYPE_SOURCE,
                id = path.sourceInfo.id,
                adapterId = path.sourceInfo.adapterId,
                width = (uint)want.Width,
                height = (uint)want.Height,
                pixelFormat = PIXELFORMAT_32BPP,
                positionX = want.X - primary.X,
                positionY = want.Y - primary.Y,
            };
            path.sourceInfo.modeInfoIdx = (uint)newModes.Count;
            newModes.Add(mode);

            // Let Windows pick the signal (refresh, timing) for the new pairing.
            path.targetInfo.modeInfoIdx = MODE_IDX_INVALID;
            path.targetInfo.refreshRate = default;
            path.targetInfo.scanLineOrdering = 0;
            path.targetInfo.rotation = want.RotationDegrees switch { 90 => 2, 180 => 3, 270 => 4, _ => 1 };
            path.flags = PATH_ACTIVE;
            chosen.Add(path);
        }

        var outPaths = chosen.ToArray();
        var outModes = newModes.ToArray();
        const uint common = SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES;
        err = SetDisplayConfig((uint)outPaths.Length, outPaths, (uint)outModes.Length, outModes, common | SDC_VALIDATE);
        if (err != 0) return (false, $"Windows rejected that arrangement (validate {err})");
        err = SetDisplayConfig((uint)outPaths.Length, outPaths, (uint)outModes.Length, outModes, common | SDC_APPLY | SDC_SAVE_TO_DATABASE);
        return err == 0 ? (true, "arranged") : (false, $"Windows could not apply that arrangement ({err})");
    }

    // --- CCD interop -------------------------------------------------------------------------------------

    private const uint QDC_ALL_PATHS = 0x1;
    private const uint SDC_VALIDATE = 0x40, SDC_APPLY = 0x80, SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20,
        SDC_SAVE_TO_DATABASE = 0x200, SDC_ALLOW_CHANGES = 0x400;
    private const uint PATH_ACTIVE = 0x1;
    private const uint MODE_IDX_INVALID = 0xFFFFFFFF;
    private const int MODE_INFO_TYPE_SOURCE = 1;
    private const int PIXELFORMAT_32BPP = 4;
    private const int DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; public long Value => ((long)HighPart << 32) | LowPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public int outputTechnology; public int rotation;
        public int scaling; public RATIONAL refreshRate; public int scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }

    /// <summary>DISPLAYCONFIG_MODE_INFO, 64 bytes; only the source-mode arm of the union is used here.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct MODE_INFO
    {
        [FieldOffset(0)] public int infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
        [FieldOffset(16)] public uint width;
        [FieldOffset(20)] public uint height;
        [FieldOffset(24)] public int pixelFormat;
        [FieldOffset(28)] public int positionX;
        [FieldOffset(32)] public int positionY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_INFO_HEADER { public int type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TARGET_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        public uint flags;
        public int outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out int numPathArrayElements, out int numModeInfoArrayElements);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref int numPathArrayElements, [Out] PATH_INFO[] pathArray, ref int numModeInfoArrayElements, [Out] MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);
    [DllImport("user32.dll")] private static extern int SetDisplayConfig(uint numPathArrayElements, [In] PATH_INFO[] pathArray, uint numModeInfoArrayElements, [In] MODE_INFO[] modeInfoArray, uint flags);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME requestPacket);
}
