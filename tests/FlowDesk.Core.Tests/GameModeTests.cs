using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class GameModeTests
{
    private sealed class Registrar : IShortcutRegistrar
    {
        public Dictionary<int, HotkeyBinding> Active { get; } = [];
        public ShortcutRegistrationResult Register(int id, HotkeyBinding binding) { Active.Add(id, binding); return ShortcutRegistrationResult.Active; }
        public bool Unregister(int id) => Active.Remove(id);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void GameModeUnregistersAndRestoresExactBindings(bool windows)
    {
        var preferences = new ShortcutPolicy(["snap-left", "desktop-move-1"]);
        var bindings = ShortcutRebinding.Effective(new Dictionary<string, string> { ["snap-right"] = "Win+Alt+J" });
        var registrations = new ShortcutRegistrations(); var native = new Registrar();
        registrations.Apply(preferences.ForModules(windows, true, true), true, native, bindings);
        var before = native.Active.Values.OrderBy(b => b.Id).ToArray();
        registrations.Apply(preferences.ForModules(windows, true, true, true), true, native, bindings);
        Assert.Equal(new[] { HotkeyCatalog.PaletteId, HotkeyCatalog.GameModeToggleId }.Order(), native.Active.Values.Select(b => b.Id).Order());
        registrations.Apply(preferences.ForModules(windows, true, true), true, native, bindings);
        Assert.Equal(before, native.Active.Values.OrderBy(b => b.Id));
        Assert.False(preferences.Enabled("snap-left"));
    }
    [Fact]
    public void RetainedBindingsStillRespectExplicitDisabledPreference()
    {
        var policy = new ShortcutPolicy([HotkeyCatalog.GameModeToggleId]).ForModules(false, true, true, true);
        Assert.False(policy.Enabled(HotkeyCatalog.GameModeToggleId));
        Assert.True(policy.Enabled(HotkeyCatalog.PaletteId));
        Assert.False(policy.Enabled(HotkeyCatalog.ClipboardId));
    }
    [Fact]
    public void TurnAllOffPreservesOnlyEscapeBindings()
    {
        var policy = new ShortcutPolicy(GameModeRules.TurnAllOffIds);
        Assert.Equal(HotkeyCatalog.All.Where(b => GameModeRules.Retained(b.Id)), HotkeyCatalog.All.Where(b => policy.Enabled(b.Id)));
    }
    [Fact]
    public void AutomaticOwnershipDoesNotClearManualChoice()
    {
        var state = new GameModeState().Sample(true, true);
        Assert.True(state.Enabled);
        Assert.False(state.Sample(true, false).Enabled);
        state = state.SetManual(true).Sample(true, false);
        Assert.True(state.Enabled); Assert.True(state.Manual);
        Assert.True(state.Sample(false, true).Enabled);
    }
    [Fact]
    public void ManualOffDuringGameSuppressesUntilGameEnds()
    {
        var state = new GameModeState().Sample(true, true).SetManual(false);
        Assert.False(state.Sample(true, true).Enabled);
        state = state.Sample(true, false);
        Assert.True(state.Sample(true, true).Enabled);
        Assert.False(new GameModeState().Sample(false, true).Enabled);
    }
    [Theory]
    [InlineData(true, false, true)] [InlineData(true, true, false)] [InlineData(false, false, false)]
    public void HookRouting(bool game, bool lonePalette, bool passthrough) => Assert.Equal(passthrough, GameModeRules.PassHooks(game, lonePalette));
    [Theory]
    [InlineData(1, 320, 64, 48)] [InlineData(1.5, 480, 96, 72)] [InlineData(2, 640, 128, 96)]
    public void FlyoutUsesPhysicalWorkAreaAndCursorMonitorScale(double scale, int width, int height, int bottom)
    {
        var rect = GameModeFlyoutRules.Place(-2560, 100, 2560, 1400, scale);
        Assert.Equal(width, rect.Width); Assert.Equal(height, rect.Height);
        Assert.Equal(-2560 + (2560 - width) / 2, rect.X);
        Assert.Equal(1500 - height - bottom, rect.Y);
    }
    [Fact]
    public void RetoggleRestartsVisibleInterval()
    {
        var lifetime = new GameModeFlyoutLifetime();
        long first = lifetime.Restart(1000, 120);
        Assert.Equal(2620, lifetime.Deadline);
        long second = lifetime.Restart(2000);
        Assert.Equal(3500, lifetime.Deadline);
        Assert.False(lifetime.IsCurrent(first)); Assert.True(lifetime.IsCurrent(second));
    }
    [Fact]
    public void WindowBoundsReturnToSavedMonitorWithNormalSize()
    {
        var primary = new ResumeBounds(0, 0, 1920, 1040); var secondary = new ResumeBounds(-2560, 0, 2560, 1400);
        Assert.Equal(new ResumeBounds(-2400, 100, 1100, 720), WindowResumeRules.Bounds(-2400, 100, 1100, 720, primary, [primary, secondary]));
        Assert.Equal(new ResumeBounds(0, 100, 1100, 720), WindowResumeRules.Bounds(-9000, 100, 1100, 720, primary, [primary]));
        Assert.Equal(new ResumeBounds(80, 80, 1100, 720), WindowResumeRules.Bounds(null, null, 1100, 720, primary, [primary]));
        Assert.Equal(new ResumeBounds(-2480, 80, 1100, 720), WindowResumeRules.OnMonitor(new(80, 80, 1100, 720), secondary, [primary, secondary]));
    }
    [Theory]
    [InlineData("Details", "Workspaces")] [InlineData("Capture", "Capture")] [InlineData("unknown", "Workspaces")]
    [InlineData("Hotkeys", "Hotkeys")] [InlineData("Settings", "Settings")]
    public void StartupPageNeedsNoWorkspaceSelection(string saved, string expected) => Assert.Equal(expected, WindowResumeRules.Page(saved));
    [Fact]
    public void HeldKeysCannotRetoggleOrStartFreshGestureUntilReleased()
    {
        var boundary = new GameModeInputBoundary();
        boundary.Seed(k => k is 0x5B or 0xA4 or 0x50 or 0x20);
        Assert.True(boundary.Contains(0x50)); // queued native P repeat must not toggle back
        Assert.True(boundary.Observe(0x50, true));
        Assert.True(boundary.Observe(0x50, false));
        Assert.False(boundary.Contains(0x50)); Assert.False(boundary.Observe(0x50, true));
        Assert.True(boundary.Observe(0x5B, false)); Assert.True(boundary.Observe(0xA4, false));
        Assert.True(boundary.Observe(0x20, false));
        Assert.False(boundary.Contains(0x5B)); Assert.False(boundary.Contains(0xA4));
        boundary.Seed(_ => false);
        Assert.False(boundary.Contains(0x20));
    }
    [Fact]
    public void SuspendedStatusDoesNotClaimWheelHooksAreActive()
    {
        var runtime = new ShortcutRuntime(true, true, true, true, ShortcutRegistrationSnapshot.Empty, GameMode: true);
        Assert.Equal(ShortcutStatus.Suspended, ShortcutPresentation.Status(HotkeyCatalog.Find("desktop-wheel")!, true, new(null), runtime));
        Assert.Equal(ShortcutStatus.Off, ShortcutPresentation.Status(HotkeyCatalog.Find("desktop-wheel")!, true, new(["desktop-wheel"]), runtime));
    }
    [Fact]
    public void ManualOffOnDesktopDoesNotSuppressTheNextGame()
    {
        var state = new GameModeState().SetManual(true).SetManual(false);
        Assert.True(state.Sample(true, true).Enabled);
    }
    [Fact]
    public void FlyoutSupportsTextScalingAndCompactOffState()
    {
        Assert.Equal(56, GameModeFlyoutRules.Place(0, 0, 1920, 1040, 1, false).Height);
        var large = GameModeFlyoutRules.Place(0, 0, 1920, 1040, 1.5, true, 2);
        Assert.Equal(960, large.Width); Assert.Equal(192, large.Height);
    }
}
