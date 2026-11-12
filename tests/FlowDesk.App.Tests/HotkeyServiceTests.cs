using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class HotkeyServiceTests
{
    [Fact]
    public async Task DisposeIsIdempotentAndLaterTransitionsDoNotStartAnything()
    {
        string dataDirectory = Path.Combine(Path.GetTempPath(), "flowdesk-hotkeys-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(dataDirectory);

        service.Dispose();
        service.Dispose();

        await service.StartAsync();
        await service.StopAsync();
        await service.ReloadBackendAsync();

        Assert.False(service.IsRunning);
        Assert.Empty(service.Conflicts);
        Assert.False(service.DesktopsAvailable);
        Assert.False(Directory.Exists(dataDirectory)); // the disposed guard returned before touching the backend
    }
}
