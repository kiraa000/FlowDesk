using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace FlowDesk.App.Services;

/// <summary>ShellWindows is a live collection. An individual dispatch object may disappear or lose
/// its members between enumeration and use. Skip that entry; never substitute an unrelated window.</summary>
internal static class ShellWindowEntry
{
    public static bool IsStale(Exception error) => error is
        RuntimeBinderException or COMException or InvalidCastException or InvalidComObjectException;

    public static object? Read(Func<object?> getter)
    {
        try { return getter(); }
        catch (Exception ex) when (IsStale(ex)) { return null; }
    }

    public static bool TryGetOwner(object? browser, out nint owner)
    {
        owner = 0;
        if (browser is null) return false;
        try
        {
            owner = (nint)(long)((dynamic)browser).HWND;
            return owner != 0;
        }
        catch (Exception ex) when (IsStale(ex)) { return false; }
    }
}
