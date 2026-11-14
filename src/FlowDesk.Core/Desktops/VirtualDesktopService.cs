namespace FlowDesk.Core.Desktops;

/// <summary>
/// One stable, shared service for workspace capture/restore and optional shortcuts. Reload replaces
/// only the native backend; the public read-only fallback and all borrowers retain this object.
/// </summary>
public sealed class VirtualDesktopService : IVirtualDesktops
{
    private readonly object gate = new();
    private readonly object reloadGate = new();
    private IVirtualDesktops native;
    private readonly IVirtualDesktops fallback;
    private readonly Func<string?, IVirtualDesktops> loadNative;
    private readonly Dictionary<string, IVirtualDesktops> loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IVirtualDesktops> owned = new(ReferenceEqualityComparer.Instance);
    private bool disposed;
    private static readonly Lazy<VirtualDesktopService> readOnly = new(() => new());
    internal static IVirtualDesktops ReadOnly => readOnly.Value;

    public VirtualDesktopService() : this(new Unavailable(), new PublicVirtualDesktops(), LoadNative) { }
    public VirtualDesktopService(IVirtualDesktops native, IVirtualDesktops fallback)
        : this(native, fallback, LoadNative) { }
    internal VirtualDesktopService(IVirtualDesktops native, IVirtualDesktops fallback, Func<string?, IVirtualDesktops> loader)
    { this.native = native; this.fallback = fallback; loadNative = loader; owned.Add(native); }

    private static IVirtualDesktops LoadNative(string? path) =>
        VirtualDesktopAccessor.Load(path is null ? [] : [path]);

    public static VirtualDesktopService Load(string appDirectory, string dataDirectory)
    {
        var service = new VirtualDesktopService();
        service.Reload(appDirectory, dataDirectory);
        return service;
    }

    public void Reload(string appDirectory, string dataDirectory)
    {
        lock (reloadGate)
        {
            lock (gate) { if (disposed) return; }
            string? path = VirtualDesktopAccessor.CandidatePaths(appDirectory, dataDirectory).FirstOrDefault(File.Exists);
            string? loadPath;
            try { loadPath = path is null ? null : DesktopLibraryCache.Prepare(path, dataDirectory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                lock (gate)
                {
                    if (disposed) return;
                    var unavailable = new Unavailable("The desktop DLL could not be prepared. Restart FlowDesk or check access to its data folder.");
                    native = unavailable;
                    owned.Add(unavailable);
                }
                return;
            }
            string key = loadPath is null ? "(missing)" : Path.GetFullPath(loadPath);
            lock (gate)
            {
                if (disposed) return;
                if (loaded.TryGetValue(key, out var existing)) { native = existing; return; }
            }
            var replacement = loadNative(loadPath);
            lock (gate)
            {
                if (disposed) { replacement.Dispose(); return; }
                native = replacement;
                loaded[key] = replacement;
                owned.Add(replacement);
            }
        }
        // Keep one STA owner per loaded library until process shutdown. Reloading the SAME DLL on a
        // second STA (or returning to a previously loaded copy) can reuse its apartment-bound COM cache.
        // A replacement at the same source path has a new immutable load path, so Settings reload works.
    }

    private T Read<T>(Func<IVirtualDesktops, T> read, T empty)
    {
        IVirtualDesktops snapshot;
        lock (gate)
        {
            if (disposed) return empty;
            snapshot = native;
        }
        // Never make UI-facing status reads wait behind another caller's native/COM timeout.
        // Backends remain alive for in-flight calls, and their Dispose rejects new queued work.
        return read(snapshot);
    }
    public bool IsAvailable => Read(n => n.IsAvailable, false);
    public string Status => Read(n => n.IsAvailable ? n.Status
        : n.Status + " Windows desktop lookup is read-only; desktop moves are unavailable.", "Desktop service has stopped.");
    public int? GetCount() => Read(n => (n.IsAvailable ? n.GetCount() : null) ?? fallback.GetCount(), (int?)null);
    public int? GetCurrent() => Read(n => (n.IsAvailable ? n.GetCurrent() : null) ?? fallback.GetCurrent(), (int?)null);
    public int? GetWindowDesktopNumber(long handle) =>
        Read(n => (n.IsAvailable ? n.GetWindowDesktopNumber(handle) : null) ?? fallback.GetWindowDesktopNumber(handle), (int?)null);
    public Guid? GetWindowDesktopId(long handle) =>
        Read(n => (n.IsAvailable ? n.GetWindowDesktopId(handle) : null) ?? fallback.GetWindowDesktopId(handle), (Guid?)null);
    public bool? IsWindowOnCurrent(long handle) =>
        Read(n => (n.IsAvailable ? n.IsWindowOnCurrent(handle) : null) ?? fallback.IsWindowOnCurrent(handle), (bool?)null);
    public bool GoTo(int index) => Read(n => n.IsAvailable && n.GoTo(index), false);
    public bool MoveWindow(long handle, int index) => Read(n => n.IsAvailable && n.MoveWindow(handle, index), false);
    public int? Create() => Read(n => n.IsAvailable ? n.Create() : null, (int?)null);

    public void Dispose()
    {
        IVirtualDesktops[] release;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (!owned.Contains(fallback)) owned.Add(fallback);
            release = owned.ToArray();
            loaded.Clear();
        }
        foreach (var provider in release)
            try { provider.Dispose(); } catch (Exception) { /* release every owned provider, even after one failure */ }
    }

    private sealed class Unavailable(string status = "Desktop support is not loaded.") : IVirtualDesktops
    {
        public bool IsAvailable => false;
        public string Status => status;
        public int? GetCount() => null;
        public int? GetCurrent() => null;
        public bool GoTo(int index) => false;
        public bool MoveWindow(long handle, int index) => false;
        public bool? IsWindowOnCurrent(long handle) => null;
        public int? Create() => null;
        public void Dispose() { }
    }
}
