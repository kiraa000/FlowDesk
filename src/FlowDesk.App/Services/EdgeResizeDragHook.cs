using System.Runtime.InteropServices;
using FlowDesk.Core.Model;

namespace FlowDesk.App.Services;

/// <summary>Owned exclusively by the hotkey message thread. END-only, so location events are never queued.</summary>
internal sealed class EdgeResizeDragHook : IDisposable
{
    private readonly WinEventProc _callback;
    private nint _hook;
    private delegate void WinEventProc(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint thread, uint time);
    public EdgeResizeDragHook(Action<bool, long> changed)
    {
        _callback = (_, eventId, hwnd, objectId, childId, _, _) =>
        {
            if (hwnd == 0 || objectId != 0 || childId != 0) return;
            try { changed(eventId == 0xA, hwnd); } catch (Exception) { /* never unwind through native callback */ }
        };
        _hook = SetWinEventHook(0xA, 0xB, 0, _callback, 0, 0, 2); // OUTOFCONTEXT | SKIPOWNPROCESS
    }
    public void Dispose()
    {
        if (_hook != 0) { UnhookWinEvent(_hook); _hook = 0; }
        GC.KeepAlive(_callback);
    }
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
