using System.Text.Json;
using FlowDesk.App.Services;
using FlowDesk.Core.Hotkeys;
using System.Reflection;

namespace FlowDesk.App.Tests;
public sealed class DisabledShortcutSettingsTests
{
    public class NoWindowCalls : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("Native window operations are forbidden in this test.");
    }
    [Fact] public void DisabledPauseAndTerminalAreIgnoredByRealHostDispatchWithoutStartingHooks()
    {
        bool opened = false;
        var windows = DispatchProxy.Create<IWindowOps, NoWindowCalls>();
        using var host = new HotkeyHost(windows, null, null, () => opened = true,
            disabledShortcutIds: ["terminal-open", HotkeyCatalog.PauseToggleId]);
        host.HandleBinding(HotkeyCatalog.Find("terminal-open")!);
        host.HandleBinding(HotkeyCatalog.Find(HotkeyCatalog.PauseToggleId)!);
        Assert.False(opened); Assert.False(host.IsPaused);
    }
    [Fact] public void ModuleOnlyHostIgnoresWindowBindingsEvenWhenPreferencesAreAllOn()
    {
        bool opened = false;
        using var host = new HotkeyHost(DispatchProxy.Create<IWindowOps, NoWindowCalls>(), null, null,
            () => opened = true, windowShortcuts: false);
        host.HandleBinding(HotkeyCatalog.Find("terminal-open")!);
        host.HandleBinding(HotkeyCatalog.Find(HotkeyCatalog.PauseToggleId)!);
        Assert.False(opened); Assert.False(host.IsPaused);
    }
    [Fact] public void OldFilesEnableEveryBinding()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"WindowShortcutsEnabled\":true}")!;
        Assert.Empty(settings.DisabledShortcuts); Assert.All(HotkeyCatalog.All, b => Assert.True(new ShortcutPolicy(settings.DisabledShortcuts).Enabled(b.Id)));
    }
    [Fact] public void IdsRoundTripAndUnknownOrNullListsAreIgnored()
    {
        var value = new AppSettings { DisabledShortcuts = ["snap-up", HotkeyCatalog.PauseToggleId, "unknown"] };
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(value))!;
        Assert.Equal(new[] { "snap-up", HotkeyCatalog.PauseToggleId }, new ShortcutPolicy(copy.DisabledShortcuts).DisabledIds);
        var nullList = JsonSerializer.Deserialize<AppSettings>("{\"DisabledShortcuts\":null}")!;
        Assert.Empty(new ShortcutPolicy(nullList.DisabledShortcuts).DisabledIds);
    }
}
