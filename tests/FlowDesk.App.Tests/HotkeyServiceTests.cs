using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class HotkeyServiceTests
{
    [Fact]
    public async Task FailedStartupCannotBeResurrectedByAnotherModule()
    {
        using var service = new HotkeyService("unused-headless-data");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestWindowShortcutsAsync(() =>
        {
            Assert.True(service.ShortcutsRequested);
            throw new InvalidOperationException("simulated host startup failure");
        }));
        Assert.False(service.ShortcutsRequested);
        await service.RequestWindowShortcutsAsync(() => Task.CompletedTask);
        Assert.True(service.ShortcutsRequested);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestWindowShortcutsAsync(() => throw new InvalidOperationException()));
        Assert.False(service.ShortcutsRequested);
    }
    [Fact]
    public async Task DisposeIsIdempotentAndLaterTransitionsDoNotStartAnything()
    {
        string dataDirectory = Path.Combine(Path.GetTempPath(), "flowdesk-hotkeys-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(dataDirectory);

        service.Dispose();
        service.Dispose();

        await service.StartAsync();
        await service.StopAsync();
        await service.StopAllAsync();
        await service.SetHotCornersAsync(new FlowDesk.Core.HotCorners.HotCornerMouseState());
        await service.SetDisabledShortcutsAsync(["pause"]);
        await service.ReloadBackendAsync();

        Assert.False(service.IsRunning);
        Assert.Empty(service.Conflicts);
        Assert.False(service.DesktopsAvailable);
        Assert.False(Directory.Exists(dataDirectory)); // the disposed guard returned before touching the backend
    }
}
