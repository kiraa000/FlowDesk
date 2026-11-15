using FlowDesk.App.Services;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Tests;

public sealed class TerminalProgramTests
{
    private const string Local = @"D:\Test\Local";
    private const string SystemFolder = @"D:\Test\System32";
    private static string? Resolve(string custom, params string[] files) => TerminalProgramResolver.Resolve(
        custom, Local, SystemFolder, [@"D:\Terminals"], p => files.Contains(p, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void CustomFullPathOrExeNameWinsOverDefault()
    {
        Assert.Equal(@"D:\Terminals\my terminal.exe", Resolve("\"D:\\Terminals\\my terminal.exe\"", @"D:\Terminals\my terminal.exe"));
        Assert.Equal(@"D:\Terminals\pwsh.exe", Resolve("pwsh", @"D:\Terminals\pwsh.exe"));
    }

    [Fact]
    public void DefaultUsesWindowsTerminalAliasThenCmd()
    {
        string wt = Path.Combine(Local, "Microsoft", "WindowsApps", "wt.exe");
        string cmd = Path.Combine(SystemFolder, "cmd.exe");
        Assert.Equal(wt, Resolve("", wt, cmd));
        Assert.Equal(cmd, Resolve(" ", cmd));
        Assert.Null(Resolve(""));
    }

    [Fact]
    public void InvalidCustomDoesNotSilentlyFallbackOrInterpretArguments()
    {
        Assert.Null(Resolve("missing.exe", Path.Combine(SystemFolder, "cmd.exe")));
        Assert.Null(Resolve("pwsh.exe -NoExit", @"D:\Terminals\pwsh.exe"));
        Assert.Null(Resolve(@".\pwsh.exe", @"D:\Terminals\pwsh.exe"));
    }

    [Fact]
    public void MissingProgramNotifiesOnlyOnceWithoutLaunchingAnything()
    {
        var terminal = new ShortcutTerminal();
        var notices = new List<string>();
        string missing = @"D:\Workspace\_tmp\opencode\definitely-missing-" + Guid.NewGuid().ToString("N") + ".exe";
        terminal.Open(missing, notices.Add);
        terminal.Open(missing, notices.Add);
        Assert.Single(notices);
        Assert.Contains("terminal", notices[0], StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ShortcutEngineCommandTests
{
    [Fact]
    public async Task StaleHostReloadCannotStartServiceAfterSettingsStoppedIt()
    {
        using var service = new HotkeyService(@"D:\Workspace\_tmp\opencode\unused-task011");
        using var oldHost = new HotkeyHost(null!, null, null);
        await service.ReloadFromHostAsync(oldHost);
        Assert.False(service.IsRunning);
        Assert.False(service.DesktopsAvailable); // no backend loaded, no disk/native calls
    }

    [Fact]
    public void ExplicitPauseIsIdempotentAndReloadAndTerminalRemainAvailable()
    {
        int reloads = 0, terminals = 0;
        var notices = new List<string>();
        // No StartAsync: these headless commands do not install hooks, inject keys, or touch windows.
        using var host = new HotkeyHost(null!, null, notices.Add, () => terminals++, () => reloads++);
        host.HandleBinding(HotkeyCatalog.Find("pause")!);
        Assert.True(host.IsPaused);
        host.HandleBinding(HotkeyCatalog.Find("pause")!);
        Assert.True(host.IsPaused);
        host.HandleBinding(HotkeyCatalog.Find("engine-reload")!);
        host.HandleBinding(HotkeyCatalog.Find("terminal-open")!);
        Assert.Equal(1, reloads);
        Assert.Equal(1, terminals);
        Assert.All(notices, n => Assert.Equal("Window shortcuts paused", n));
        host.HandleBinding(HotkeyCatalog.Find("pause-toggle")!);
        Assert.False(host.IsPaused);
        Assert.Equal("Window shortcuts on", notices[^1]);
    }
}
