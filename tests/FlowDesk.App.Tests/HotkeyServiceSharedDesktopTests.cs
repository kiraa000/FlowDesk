using FlowDesk.App.Services;
using FlowDesk.Core.Desktops;

namespace FlowDesk.App.Tests;

/// <summary>
/// The app window owns one desktop backend. The optional shortcut service borrows it and must never
/// dispose it while stopping or disposing itself; the owner disposes once after its borrowers.
/// </summary>
public sealed class HotkeyServiceSharedDesktopTests
{
    [Fact]
    public async Task Borrowed_shared_desktop_service_is_never_disposed_by_hotkeys()
    {
        string dataDirectory = Path.Combine(Path.GetTempPath(), "flowdesk-hotkeys-shared-" + Guid.NewGuid().ToString("N"));
        var native = new DisposeCountingDesktops();
        var fallback = new DisposeCountingDesktops();
        var shared = new VirtualDesktopService(native, fallback);
        var service = new HotkeyService(dataDirectory, shared);

        await service.StopAsync();
        service.Dispose();
        service.Dispose();

        Assert.Equal(0, native.DisposeCount);
        Assert.Equal(0, fallback.DisposeCount);
        Assert.Equal(2, shared.GetCount()); // still usable for capture/restore until the owner disposes

        shared.Dispose();
        Assert.Equal(1, native.DisposeCount);
        Assert.Equal(1, fallback.DisposeCount);
        Assert.False(Directory.Exists(dataDirectory)); // no hook/native/reload work was started
    }

    private sealed class DisposeCountingDesktops : IVirtualDesktops
    {
        public bool IsAvailable => true;
        public string Status => "Desktop fake ready.";
        public int DisposeCount { get; private set; }
        public int? GetCount() => 2;
        public int? GetCurrent() => 0;
        public bool GoTo(int index) => true;
        public bool MoveWindow(long handle, int index) => true;
        public bool? IsWindowOnCurrent(long handle) => true;
        public int? Create() => 3;
        public int? GetWindowDesktopNumber(long handle) => 0;
        public Guid? GetWindowDesktopId(long handle) => null;
        public void Dispose() => DisposeCount++;
    }
}
