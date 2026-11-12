using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace FlowDesk.Core.Desktops;

/// <summary>
/// <see cref="IVirtualDesktops"/> backed by VirtualDesktopAccessor.dll (MIT, github.com/Ciantic/VirtualDesktopAccessor,
/// the Rust build for Windows 11 24H2+). FlowDesk does not ship the DLL; it is loaded from
/// <see cref="CandidatePaths"/>. All calls run on one private STA thread (the DLL uses COM and the
/// explorer-side objects are apartment bound) and each call waits at most <see cref="CallTimeout"/>,
/// so a hung explorer cannot freeze the caller (the hotkey worker).
/// </summary>
public sealed unsafe class VirtualDesktopAccessor : IVirtualDesktops
{
    public const string DllName = "VirtualDesktopAccessor.dll";
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);

    private readonly nint _library;
    private readonly delegate* unmanaged<int> _getCount;
    private readonly delegate* unmanaged<int> _getCurrent;
    private readonly delegate* unmanaged<int, int> _goTo;
    private readonly delegate* unmanaged<nint, int, int> _moveWindow;
    private readonly delegate* unmanaged<nint, int> _isOnCurrent;
    private readonly delegate* unmanaged<int> _create; // optional: older DLL builds lack it

    private readonly BlockingCollection<Action>? _queue;
    private readonly Thread? _thread;
    private int _disposed;

    public bool IsAvailable { get; }
    public string Status { get; }
    public string? LoadedPath { get; }

    /// <summary>Where the DLL is looked for, in order: next to FlowDesk, then the data folder's native\ folder.</summary>
    public static IReadOnlyList<string> CandidatePaths(string appDirectory, string dataDirectory) =>
    [
        Path.Combine(appDirectory, DllName),
        Path.Combine(dataDirectory, "native", DllName),
    ];

    public static VirtualDesktopAccessor Load(IEnumerable<string> candidatePaths)
    {
        string? path = candidatePaths.FirstOrDefault(File.Exists);
        return new VirtualDesktopAccessor(path);
    }

    private VirtualDesktopAccessor(string? path)
    {
        if (path is null)
        {
            Status = "Desktop shortcuts need VirtualDesktopAccessor.dll. Choose the file to turn them on.";
            return;
        }
        if (!NativeLibrary.TryLoad(path, out _library))
        {
            Status = "VirtualDesktopAccessor.dll could not be loaded. Use the 64-bit build for this version of Windows.";
            return;
        }

        nint count = Export("GetDesktopCount"), current = Export("GetCurrentDesktopNumber"),
             goTo = Export("GoToDesktopNumber"), move = Export("MoveWindowToDesktopNumber"),
             onCurrent = Export("IsWindowOnCurrentVirtualDesktop");
        if (count == 0 || current == 0 || goTo == 0 || move == 0 || onCurrent == 0)
        {
            Status = "This VirtualDesktopAccessor.dll is missing functions FlowDesk needs. Use a newer build.";
            return;
        }

        _getCount = (delegate* unmanaged<int>)count;
        _getCurrent = (delegate* unmanaged<int>)current;
        _goTo = (delegate* unmanaged<int, int>)goTo;
        _moveWindow = (delegate* unmanaged<nint, int, int>)move;
        _isOnCurrent = (delegate* unmanaged<nint, int>)onCurrent;
        _create = (delegate* unmanaged<int>)Export("CreateDesktop");

        _queue = new BlockingCollection<Action>();
        _thread = new Thread(Pump) { IsBackground = true, Name = "FlowDesk virtual desktops" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        LoadedPath = path;
        IsAvailable = true;

        // A DLL built for another Windows build loads fine but fails every call: probe once.
        int? probe = GetCount();
        if (probe is null or < 1)
        {
            IsAvailable = false;
            Status = "VirtualDesktopAccessor.dll loaded but does not work on this Windows build. Use a newer build.";
            return;
        }
        Status = "Desktop shortcuts are ready.";
    }

    private nint Export(string name) => NativeLibrary.TryGetExport(_library, name, out nint address) ? address : 0;

    public int? GetCount() => Call(() => _getCount(), out int n) && n >= 1 ? n : null;

    public int? GetCurrent() => Call(() => _getCurrent(), out int n) && n >= 0 ? n : null;

    public bool GoTo(int index) => index >= 0 && Call(() => _goTo(index), out int r) && r != -1;

    public bool MoveWindow(long handle, int index) =>
        handle != 0 && index >= 0 && Call(() => _moveWindow((nint)handle, index), out int r) && r != -1;

    public bool? IsWindowOnCurrent(long handle)
    {
        if (handle == 0 || !Call(() => _isOnCurrent((nint)handle), out int r) || r == -1) return null;
        return r == 1;
    }

    public int? Create()
    {
        if (_create == null) return null;
        int? before = GetCount();
        if (before is null || !Call(() => _create(), out int r) || r == -1) return null;
        int? after = GetCount();
        return after > before ? after : null;
    }

    private bool Call(Func<int> body, out int result)
    {
        result = -1;
        if (!IsAvailable || _queue is null || Volatile.Read(ref _disposed) != 0) return false;
        if (Thread.CurrentThread == _thread)
        {
            result = SafeInvoke(body);
            return result != int.MinValue;
        }

        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { _queue.Add(() => done.TrySetResult(SafeInvoke(body))); }
        catch (InvalidOperationException) { return false; } // disposed meanwhile
        if (!done.Task.Wait(CallTimeout)) return false;
        result = done.Task.Result;
        return result != int.MinValue;
    }

    private static int SafeInvoke(Func<int> body)
    {
        try { return body(); }
        catch (Exception) { return int.MinValue; } // SEH from a mismatched DLL surfaces as SEHException
    }

    private void Pump()
    {
        foreach (Action work in _queue!.GetConsumingEnumerable())
        {
            try { work(); } catch { /* SafeInvoke already contains failures */ }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue?.CompleteAdding();
        // The library stays loaded: a call may still be running on the pump thread, and the
        // process exits shortly after Dispose in practice.
    }
}
