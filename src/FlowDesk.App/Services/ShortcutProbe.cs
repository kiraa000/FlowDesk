using System.Runtime.InteropServices;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Services;

public enum ShortcutProbeResult { Available, Conflict, Unavailable, HookUnknown, OwnedByFlowDesk }
public interface IShortcutProbe
{
    Task<ShortcutProbeResult> ProbeAsync(KeyChord chord);
}
public sealed class ShortcutProbe : IShortcutProbe
{
    public Task<ShortcutProbeResult> ProbeAsync(KeyChord chord) => Task.Run(() =>
    {
        // A thread-local, immediately released test registration. No window, hook or input synthesis.
        const int probeId = 0xBFFE;
        if (!RegisterHotKey(0, probeId, chord.Modifiers | 0x4000, chord.VirtualKey))
            return Marshal.GetLastWin32Error() == 1409 ? ShortcutProbeResult.Conflict : ShortcutProbeResult.Unavailable;
        return UnregisterHotKey(0, probeId) ? ShortcutProbeResult.Available : ShortcutProbeResult.Unavailable;
    });
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
public static class ShortcutProbePresentation
{
    public static bool Blocks(ShortcutProbeResult result) => result is ShortcutProbeResult.Conflict or ShortcutProbeResult.Unavailable;
    public static string Text(ShortcutProbeResult result) => result switch
    {
        ShortcutProbeResult.Conflict => "In use by another app",
        ShortcutProbeResult.Unavailable => "Windows could not check this shortcut. Try another combination.",
        ShortcutProbeResult.HookUnknown => "Hook-based input cannot be checked for conflicts with other apps.",
        _ => "Availability checked. Windows will check again when you save."
    };
}
