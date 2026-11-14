using System.Security.Cryptography;
using FlowDesk.Core.Desktops;

namespace FlowDesk.Core.Tests;

/// <summary>
/// The shared service uses the native DLL when it can, and the public COM manager as a read-only
/// fallback. Tests inject both backends; they never construct the real service (no COM/DLL side effects).
/// </summary>
public class VirtualDesktopServiceTests
{
    private static readonly Guid DesktopB = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    [Fact]
    public void Native_failed_reads_fall_back_to_the_com_backend()
    {
        var native = new FakeVirtualDesktops { Available = false, ReadsReturnNull = true };
        var com = new FakeVirtualDesktops { Available = true, Count = 4, Current = 1 };
        const long handle = 7;
        com.DesktopNumbers[handle] = 2;
        com.DesktopIds[handle] = DesktopB;
        com.OnCurrent[handle] = false;
        var service = new VirtualDesktopService(native, com);

        Assert.Equal(2, service.GetWindowDesktopNumber(handle));
        Assert.Equal(DesktopB, service.GetWindowDesktopId(handle));
        Assert.Equal(1, service.GetCurrent());
        Assert.Equal(4, service.GetCount());
        Assert.False(service.IsWindowOnCurrent(handle));
    }

    [Fact]
    public void Native_values_win_over_the_fallback()
    {
        var native = new FakeVirtualDesktops { Count = 3, Current = 0 };
        var com = new FakeVirtualDesktops { Count = 9, Current = 8 };
        const long handle = 8;
        var nativeId = Guid.Parse("dddddddd-0000-0000-0000-000000000004");
        native.DesktopNumbers[handle] = 1;
        native.DesktopIds[handle] = nativeId;
        com.DesktopNumbers[handle] = 2;
        com.DesktopIds[handle] = DesktopB;
        var service = new VirtualDesktopService(native, com);

        Assert.Equal(1, service.GetWindowDesktopNumber(handle));
        Assert.Equal(nativeId, service.GetWindowDesktopId(handle));
        Assert.Equal(0, service.GetCurrent());
        Assert.Equal(3, service.GetCount());
    }

    [Fact]
    public void A_backend_without_the_optional_desktop_lookup_still_works()
    {
        // MinimalVirtualDesktops only implements the original members: the new lookups are default
        // interface members returning null, so the service must fall through without a crash.
        var native = new MinimalVirtualDesktops();
        var com = new FakeVirtualDesktops { Count = 2, Current = 0 };
        const long handle = 9;
        com.DesktopNumbers[handle] = 1;
        com.DesktopIds[handle] = DesktopB;
        var service = new VirtualDesktopService(native, com);

        Assert.Equal(1, service.GetWindowDesktopNumber(handle));
        Assert.Equal(DesktopB, service.GetWindowDesktopId(handle));
    }

    [Fact]
    public void Dispose_disposes_each_injected_backend_once()
    {
        var native = new FakeVirtualDesktops();
        var com = new FakeVirtualDesktops();
        var service = new VirtualDesktopService(native, com);

        service.Dispose();
        service.Dispose();

        Assert.Equal(1, native.DisposeCount);
        Assert.Equal(1, com.DisposeCount);
    }

    [Fact]
    public void Dispose_does_not_dispose_a_shared_backend_twice()
    {
        var shared = new FakeVirtualDesktops();
        var service = new VirtualDesktopService(shared, shared);

        service.Dispose();

        Assert.Equal(1, shared.DisposeCount);
    }

    [Fact]
    public void IsAvailable_means_the_native_backend_can_move_windows()
    {
        Assert.False(new VirtualDesktopService(
            new FakeVirtualDesktops { Available = false },
            new FakeVirtualDesktops { Available = true }).IsAvailable);

        Assert.True(new VirtualDesktopService(
            new FakeVirtualDesktops { Available = true },
            new FakeVirtualDesktops { Available = false }).IsAvailable);
    }

    [Fact]
    public void Same_source_bytes_reload_loads_the_immutable_copy_once()
    {
        using var h = new ReloadHarness { Factory = _ => new FakeVirtualDesktops { Count = 7 } };
        h.CreateBundle();

        h.Reload();
        h.Reload();

        Assert.Equal(1, h.Calls);
        Assert.Equal(7, h.Service.GetCount());
        string loaded = Assert.Single(h.Paths)!;
        Assert.Equal(h.CachePath(h.BundleBytes), loaded);
        Assert.NotEqual(Path.GetFullPath(h.BundlePath), Path.GetFullPath(loaded));
        Assert.Equal(h.BundleBytes, File.ReadAllBytes(loaded));
        Assert.Contains(Hash(h.BundleBytes), loaded);
    }

    [Fact]
    public void Override_appears_wins_then_removal_returns_to_the_cached_bundle()
    {
        var bundle = new FakeVirtualDesktops { Count = 7 };
        var over = new FakeVirtualDesktops { Count = 9 };
        using var h = new ReloadHarness();
        h.Factory = path =>
        {
            if (Same(path, h.CachePath(h.BundleBytes))) return bundle;
            if (Same(path, h.CachePath(h.OverrideBytes))) return over;
            throw new InvalidOperationException("Unexpected DLL path: " + path);
        };
        h.CreateBundle();
        h.Reload();
        Assert.Equal(7, h.Service.GetCount());

        h.CreateOverride();
        h.Reload();
        Assert.Equal(9, h.Service.GetCount());

        h.RemoveOverride();
        h.Reload();
        Assert.Equal(7, h.Service.GetCount()); // cached bundle provider, no reload
        Assert.Equal(2, h.Calls);
        Assert.All(h.Paths, p => Assert.False(Same(p, h.BundlePath) || Same(p, h.OverridePath)));
    }

    [Fact]
    public void Owner_disposes_every_unique_provider_once()
    {
        var bundle = new FakeVirtualDesktops { Count = 7 };
        var over = new FakeVirtualDesktops { Count = 9 };
        using var h = new ReloadHarness();
        h.Factory = path => Same(path, h.CachePath(h.BundleBytes)) ? bundle
            : Same(path, h.CachePath(h.OverrideBytes)) ? over
            : throw new InvalidOperationException("Unexpected DLL path: " + path);
        h.CreateBundle();
        h.Reload();
        h.CreateOverride();
        h.Reload();

        h.Service.Dispose();
        h.Service.Dispose();

        Assert.Equal(1, h.Initial.DisposeCount);
        Assert.Equal(1, h.Fallback.DisposeCount);
        Assert.Equal(1, bundle.DisposeCount);
        Assert.Equal(1, over.DisposeCount);
    }

    [Fact]
    public void Reload_after_dispose_neither_loads_nor_resurrects()
    {
        using var h = new ReloadHarness { Factory = _ => new FakeVirtualDesktops { Count = 7 } };
        h.CreateBundle();
        h.Reload();
        Assert.Equal(7, h.Service.GetCount());

        h.Service.Dispose();
        h.Reload();
        h.Reload();

        Assert.Equal(1, h.Calls);
        Assert.Null(h.Service.GetCount());
        Assert.False(h.Service.IsAvailable);
    }

    [Fact]
    public void Concurrent_reload_of_the_same_path_uses_one_provider()
    {
        var provider = new FakeVirtualDesktops { Count = 7 };
        using var h = new ReloadHarness { Factory = _ => provider };
        h.CreateBundle();

        Parallel.For(0, 8, _ => h.Reload());

        Assert.Equal(1, h.Calls);
        Assert.Equal(7, h.Service.GetCount());
    }

    [Fact]
    public void Loader_failure_keeps_the_previous_provider_usable()
    {
        var bundle = new FakeVirtualDesktops { Count = 42 };
        using var h = new ReloadHarness();
        h.Factory = path => Same(path, h.CachePath(h.OverrideBytes))
            ? throw new IOException("fake DLL load failure")
            : bundle;
        h.CreateBundle();
        h.Reload();
        Assert.Equal(42, h.Service.GetCount());

        h.CreateOverride();
        Assert.Throws<IOException>(() => h.Reload());

        Assert.Equal(42, h.Service.GetCount());
        Assert.True(h.Service.IsAvailable);
    }

    [Fact]
    public void Rewriting_the_same_fixed_source_bad_to_good_gets_a_new_copy_and_provider()
    {
        byte[] bad = [0x01, 0x02], good = [0x03, 0x04];
        using var h = new ReloadHarness();
        h.Factory = path => Same(path, h.CachePath(bad))
            ? new FakeVirtualDesktops { Available = false, Count = 5 }
            : new FakeVirtualDesktops { Available = true, Count = 9 };
        h.CreateOverride(bad);

        h.Reload();
        Assert.Equal(1, h.Calls);
        Assert.False(h.Service.IsAvailable);
        Assert.True(File.Exists(h.CachePath(bad)));

        h.WriteOverride(good); // the fixed user path is replaced, not the loaded copy
        h.Reload();

        Assert.Equal(2, h.Calls);
        Assert.True(h.Service.IsAvailable);
        Assert.Equal(9, h.Service.GetCount());
        Assert.NotEqual(h.CachePath(bad), h.CachePath(good));
        Assert.True(File.Exists(h.CachePath(good)));
        Assert.Equal(good, File.ReadAllBytes(h.OverridePath));
    }

    [Fact]
    public void Equal_bytes_at_another_source_path_reuses_one_provider()
    {
        byte[] bytes = [0x07, 0x08];
        using var h = new ReloadHarness { Factory = _ => new FakeVirtualDesktops { Count = 7 } };
        h.CreateBundle(bytes);
        h.Reload();
        Assert.Equal(1, h.Calls);

        h.CreateOverride(bytes); // alias source, identical bytes
        h.Reload();

        Assert.Equal(1, h.Calls);
        Assert.Equal(h.CachePath(bytes), Assert.Single(h.Paths));
        Assert.Equal(7, h.Service.GetCount());
    }

    [Fact]
    public void Corrupt_existing_cached_copy_falls_back_without_loading_or_overwriting()
    {
        byte[] bytes = [0x0A, 0x0B];
        using var h = new ReloadHarness { Factory = _ => new FakeVirtualDesktops { Count = 7 } };
        h.CreateBundle(bytes);
        h.Reload();
        string cache = h.CachePath(bytes);
        Assert.True(File.Exists(cache));
        byte[] corrupt = [0xEE, 0xEE];
        File.WriteAllBytes(cache, corrupt);

        h.Reload();

        Assert.Equal(1, h.Calls); // the loader never ran for the damaged copy
        Assert.False(h.Service.IsAvailable);
        Assert.Equal(2, h.Service.GetCount()); // fallback remains usable
        Assert.Equal(corrupt, File.ReadAllBytes(cache)); // prior cached file untouched
    }

    private static bool Same(string? path, string fixture) =>
        path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(fixture), StringComparison.OrdinalIgnoreCase);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Own temp fixture: distinct dummy bytes mark the candidate paths; only the service's immutable
    /// copy path reaches the injected factory. The production loader and COM/DLL code are never used.
    /// </summary>
    private sealed class ReloadHarness : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flowdesk-vdsvc-" + Guid.NewGuid().ToString("N"));
        private readonly List<string?> _paths = [];
        public FakeVirtualDesktops Initial { get; } = new() { Count = 1 };
        public FakeVirtualDesktops Fallback { get; } = new() { Count = 2 };
        public Func<string?, IVirtualDesktops> Factory { get; set; } = _ => new FakeVirtualDesktops { Count = 3 };
        public VirtualDesktopService Service { get; }
        public byte[] BundleBytes { get; } = [0x11, 0x12, 0x13];
        public byte[] OverrideBytes { get; } = [0x21, 0x22, 0x23];

        public ReloadHarness()
        {
            AppDir = Path.Combine(_root, "app");
            DataDir = Path.Combine(_root, "data");
            Service = new VirtualDesktopService(Initial, Fallback, path =>
            {
                lock (_paths) _paths.Add(path);
                return Factory(path);
            });
        }

        public string AppDir { get; }
        public string DataDir { get; }
        public string BundlePath => Path.Combine(AppDir, VirtualDesktopAccessor.DllName);
        public string OverridePath => Path.Combine(DataDir, "native", VirtualDesktopAccessor.DllName);
        public int Calls { get { lock (_paths) return _paths.Count; } }
        public IReadOnlyList<string?> Paths { get { lock (_paths) return _paths.ToArray(); } }

        public string CachePath(byte[] bytes) =>
            Path.Combine(DataDir, "native", "cache", Hash(bytes), VirtualDesktopAccessor.DllName);

        public void CreateBundle(byte[]? bytes = null) => Write(BundlePath, bytes ?? BundleBytes);
        public void CreateOverride(byte[]? bytes = null) => Write(OverridePath, bytes ?? OverrideBytes);
        public void WriteOverride(byte[] bytes) => Write(OverridePath, bytes);
        public void RemoveOverride() => File.Delete(OverridePath);
        public void Reload() => Service.Reload(AppDir, DataDir);

        private static void Write(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
