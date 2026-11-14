using System.Runtime.InteropServices;

namespace FlowDesk.App.Services;

/// <summary>Windows session end is not a user clicking X. Never veto shutdown or sign-out.</summary>
internal sealed class SessionEndHook : IDisposable
{
    private readonly nint window;
    private readonly SubclassProc callback;
    private readonly Action endSession;
    public SessionEndHook(nint window, Action endSession)
    {
        this.window = window; this.endSession = endSession; callback = Process;
        if (!SetWindowSubclass(window, callback, 9009, 0))
            throw new InvalidOperationException("Session-end handler could not be installed.");
    }
    private nint Process(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0011) return 1; // WM_QUERYENDSESSION: no dialogs, no waits, no veto
        if (message == 0x0016 && wParam != 0)
        {
            try { endSession(); } // WM_ENDSESSION (not a cancelled shutdown)
            catch (Exception) { /* No managed exception may cross a native window procedure. */ }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    public void Dispose() => RemoveWindowSubclass(window, callback, 9009);
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
