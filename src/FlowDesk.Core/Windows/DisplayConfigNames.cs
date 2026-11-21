using System.Runtime.InteropServices;

namespace FlowDesk.Core.Windows;

/// <summary>Optional active DisplayConfig metadata joined to EnumDisplayMonitors by GDI source name.</summary>
internal static class DisplayConfigNames
{
    internal sealed record Metadata(string FriendlyName, string StableId, bool IsInternal);

    internal static IReadOnlyDictionary<string, Metadata> Read()
    {
        var result = new Dictionary<string, Metadata>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Topology may change between sizing and querying. Retry only the documented buffer race.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (GetDisplayConfigBufferSizes(2, out uint paths, out uint modes) != 0) return result;
                if (paths > 4096 || modes > 16384) return result;
                var pathData = new PathInfo[paths]; var modeData = new ModeInfo[modes];
                int error = QueryDisplayConfig(2, ref paths, pathData, ref modes, modeData, 0);
                if (error == 122) continue;
                if (error != 0) return result;
                for (int i = 0; i < paths; i++)
                {
                    var path = pathData[i];
                    var source = new SourceName { Header = Header.For<SourceName>(1, path.Source.Adapter, path.Source.Id) };
                    var target = new TargetName { Header = Header.For<TargetName>(2, path.Target.Adapter, path.Target.Id) };
                    if (GetSourceName(ref source) != 0 || GetTargetName(ref target) != 0) continue;
                    // Mirrored targets share a GDI source; take the first deterministically, not the last.
                    result.TryAdd(source.Name, new(target.FriendlyName.Trim(), target.DevicePath.Trim(),
                        target.Technology is 0x80000000 or 6 or 11 or 13)); // INTERNAL, LVDS, embedded DisplayPort / UDI
                }
                return result;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or
            ExternalException or ArgumentException or OverflowException) { /* optional metadata, never block capture */ }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct SourceInfo
    { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct TargetInfo
    {
        public Luid Adapter;
        public uint Id, ModeIndex, Technology, Rotation, Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering, Available, Status;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo
    { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] private struct ModeInfo { }
    [StructLayout(LayoutKind.Sequential)] private struct Header
    {
        public uint Type, Size; public Luid Adapter; public uint Id;
        public static Header For<T>(uint type, Luid adapter, uint id) =>
            new() { Type = type, Size = (uint)Marshal.SizeOf<T>(), Adapter = adapter, Id = id };
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceName
    { public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName
    {
        public Header Header;
        public uint Flags, Technology;
        public ushort Manufacturer, Product;
        public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths,
        [Out] PathInfo[] pathData, ref uint modes, [Out] ModeInfo[] modeData, nint topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetSourceName(ref SourceName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int GetTargetName(ref TargetName name);
}
