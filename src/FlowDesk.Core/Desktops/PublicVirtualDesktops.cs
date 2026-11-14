using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace FlowDesk.Core.Desktops;

/// <summary>
/// Documented IVirtualDesktopManager read-only fallback. COM is created/used on a private STA,
/// never on a UI thread or a caller's apartment. No MoveWindowToDesktop calls for foreign apps.
/// </summary>
internal sealed class PublicVirtualDesktops : IVirtualDesktops
{
    private readonly object gate = new();
    private BlockingCollection<Action>? queue;
    private IVirtualDesktopManager? manager;
    private bool disposed;
    private int pending;
    private long retryAfter;
    public bool IsAvailable => false; // Windows' public API cannot move other apps' windows
    public string Status => "Windows can identify a window's desktop but cannot move other apps' windows.";
    public int? GetCount() => null;
    public int? GetCurrent() => null;
    public int? Create() => null;
    public bool GoTo(int index) => false;
    public bool MoveWindow(long handle, int index) => false;

    public Guid? GetWindowDesktopId(long handle) => handle == 0 ? null : Query<Guid>(m =>
        m.GetWindowDesktopId((nint)handle, out var id) >= 0 && id != Guid.Empty ? id : null);
    public bool? IsWindowOnCurrent(long handle) => handle == 0 ? null : Query<bool>(m =>
        m.IsWindowOnCurrentVirtualDesktop((nint)handle, out bool current) >= 0 ? current : null);

    private T? Query<T>(Func<IVirtualDesktopManager, T?> read) where T : struct
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref retryAfter)
            || Interlocked.CompareExchange(ref pending, 1, 0) != 0) return null;
        var done = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (disposed) { Interlocked.Exchange(ref pending, 0); return null; }
            if (queue is null)
            {
                queue = new();
                var thread = new Thread(Pump) { IsBackground = true, Name = "FlowDesk public desktop lookup" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
            queue.Add(() =>
            {
                T? value = null;
                try
                {
                    manager ??= (IVirtualDesktopManager)Activator.CreateInstance(
                        Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), throwOnError: true)!)!;
                    value = read(manager);
                }
                catch (Exception)
                {
                    // A transient server/initialization failure may recover. Back off rather than
                    // disabling public desktop capture for the rest of the process lifetime.
                    Interlocked.Exchange(ref retryAfter, DateTime.UtcNow.AddSeconds(2).Ticks);
                }
                finally { Interlocked.Exchange(ref pending, 0); done.TrySetResult(value); }
            });
        }
        if (done.Task.Wait(TimeSpan.FromMilliseconds(500))) return done.Task.Result;
        // At most one in-flight COM request: no N-window timeout/queue pile-up. Once it completes,
        // a later refresh can retry; a timeout is not permanently latched as "no desktop support".
        return null;
    }

    private void Pump()
    {
        try { foreach (var read in queue!.GetConsumingEnumerable()) read(); }
        finally
        {
            try { if (manager is not null && Marshal.IsComObject(manager)) Marshal.FinalReleaseComObject(manager); }
            catch (Exception) { /* shutdown must not turn failed diagnostics into an unhandled exception */ }
        }
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; queue?.CompleteAdding(); }
    }

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint window, out Guid id);
        [PreserveSig] int MoveWindowToDesktop(nint window, [In] ref Guid id);
    }
}
