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

    // ---- TASK-033B: host-order sequences over the real hotkey state classes ----

    private sealed class ManualClock
    {
        public long Now { get; private set; } = 1;
        public void Advance(long milliseconds) => Now += milliseconds;
    }

    private sealed class NativeTable : IShortcutRegistrar
    {
        public Dictionary<int, HotkeyBinding> Active { get; } = [];
        public ShortcutRegistrationResult Register(int id, HotkeyBinding binding)
        { Active.Add(id, binding); return ShortcutRegistrationResult.Active; }
        public bool Unregister(int id) => Active.Remove(id);
    }

    /// <summary>
    /// Replays HotkeyHost's real order with the real classes: the keyboard hook observes
    /// repeat gate, keyboard state and mode boundary before the early PassHooks return;
    /// WM_HOTKEY runs the boundary, policy, game-mode retention and repeat-gate checks in
    /// that order; a mode change re-applies registrations to the retained set, then seeds
    /// the boundary and keyboard state and resets the repeat gate and ownership.
    /// </summary>
    private sealed class HostSequence
    {
        private readonly IReadOnlyList<HotkeyBinding> bindings;
        private readonly ShortcutPolicy policy = new([]);
        // What GetAsyncKeyState reports. A low-level hook that returns 1 discards the event
        // before Windows updates this state, so an owned paired up leaves the key "down" here
        // even though the hook saw the release.
        private readonly HashSet<int> asyncDown = [];
        private readonly ShortcutKeyboardState keyboard;
        private readonly ShortcutRepeatGate repeat = new();
        private readonly GameModeInputBoundary boundary = new();
        private readonly HotkeyInputOwnership ownership = new();
        private readonly ShortcutRegistrations registrations = new();
        private readonly NativeTable native = new();
        private readonly ManualClock clock = new();
        private readonly bool legacyAsyncBoundary;
        public bool PaletteWindowsKey { get; }
        public bool Palette { get; } = true;
        public bool WindowShortcuts { get; } = true;
        public bool KeyboardHook { get; } = true;
        public bool GameMode { get; private set; }
        public int DeliveredHotkeys { get; private set; }
        public long Now => clock.Now;
        public IReadOnlySet<string> RegisteredIds => registrations.RegisteredIds;
        public HotkeyBinding ToggleBinding => bindings.Single(b => b.Id == HotkeyCatalog.GameModeToggleId);
        public bool HookDown(uint key) => repeat.IsDown((int)key);
        public bool BoundaryHeld(uint key) => boundary.Contains(key);
        public bool AsyncDown(uint key) => asyncDown.Contains((int)key);

        public HostSequence(bool paletteWindowsKey, IReadOnlyDictionary<string, string>? overrides = null,
            bool legacyAsyncBoundary = false)
        {
            PaletteWindowsKey = paletteWindowsKey;
            this.legacyAsyncBoundary = legacyAsyncBoundary;
            bindings = ShortcutRebinding.Effective(overrides);
            keyboard = new ShortcutKeyboardState(() => clock.Now);
            ApplyRegistrations(false); // startup registration; never a mode boundary
        }

        private bool SoftwareRepeat => WindowShortcuts && KeyboardHook;
        private ShortcutModifiers Modifiers => keyboard.Modifiers;
        private uint Mods() => (Modifiers.Win ? 8u : 0) | (Modifiers.Alt ? 1u : 0) |
            (Modifiers.Control ? 2u : 0) | (Modifiers.Shift ? 4u : 0);
        private bool IsDown(int key) => asyncDown.Contains(key);
        private void ApplyRegistrations(bool gameMode) => registrations.Apply(
            policy.ForModules(WindowShortcuts, Palette, clipboard: false, gameMode), true, native, bindings);

        public void Advance(long milliseconds) => clock.Advance(milliseconds);

        /// <summary>ApplyBindingChange while the host switches game mode.</summary>
        public void ApplyGameMode(bool enabled)
        {
            if (enabled == GameMode) return;
            ApplyRegistrations(enabled);
            // HotkeyHost.cs:234 masks an already-forwarded Alt/Win hold with tagged vkE8
            // events; those touch neither a tracked modifier nor the toggle key.
            // Exact production call: hook-observed held set unioned with keys still held at the
            // previous boundary (a second mode transition can precede their up; the gate was reset).
            // The legacy switch replays the pre-fix async seed for the negative control only.
            Func<int, bool> seed = legacyAsyncBoundary
                ? IsDown
                : key => repeat.IsDown(key) || boundary.Contains((uint)key);
            boundary.Seed(seed);
            keyboard.Seed(IsDown);
            repeat.Reset();
            ownership.Reset();
            GameMode = enabled;
        }

        public void Down(uint key) => Observe(key, down: true);
        public void Up(uint key) => Observe(key, down: false);

        private void Observe(uint key, bool down)
        {
            // HotkeyHost.cs:703-705: the real hook order, before any early return.
            repeat.Observe(key, down);
            var observed = keyboard.Observe((int)key, down, 0);
            bool atBoundary = boundary.Observe(key, down);
            bool swallowed = false;
            if (!atBoundary && !GameModeRules.PassHooks(GameMode, PaletteWindowsKey) && !observed.OwnInput)
            {
                HotkeyBinding? matched = null;
                if (down && !KeyChord.IsModifier(key))
                    foreach (int id in registrations.NativeIds)
                        if (registrations.TryGet(id, out var binding) && binding.VirtualKey == key && binding.Modifiers == Mods())
                        { matched = binding; break; }
                var owned = GameMode ? default : ownership.Observe(key, down, Now, matched);
                swallowed = owned.Swallow; // HotkeyHost.cs:740,749
                ownership.TakeAltMask(key, down, Now);
            }
            if (swallowed) return; // the system discards this event, so async state keeps its old value
            if (down) asyncDown.Add((int)key); else asyncDown.Remove((int)key);
        }

        /// <summary>A registered chord's key-down produces one WM_HOTKEY; unregistered chords none.</summary>
        public bool Trigger(uint key)
        {
            HotkeyBinding? binding = null;
            foreach (int id in registrations.NativeIds)
                if (registrations.TryGet(id, out var candidate) && candidate.VirtualKey == key && candidate.Modifiers == Mods())
                { binding = candidate; break; }
            if (binding is null) return false;
            DeliveredHotkeys++;
            return OnHotkey(binding);
        }

        private bool OnHotkey(HotkeyBinding binding)
        {
            if (KeyboardHook && boundary.Contains(binding.VirtualKey)) return false;
            if (!policy.Enabled(binding.Id)) return false;
            if (GameMode && !GameModeRules.Retained(binding.Id)) return false;
            if (SoftwareRepeat && !repeat.Accept(binding)) return false;
            return true;
        }
    }

    private static readonly uint Ctrl = 0xA2, Alt = 0xA4, G = 0x47, A = 0x41, Left = 0x25;

    /// <summary>The app applies the mode change on the UI thread; it may land while the chord is
    /// still held or only after every key was released. Both boundary variants must stay usable.</summary>
    private static bool PressToggle(HostSequence host, bool boundaryWhileHeld)
    {
        uint[] chord = ToggleChord(host);
        for (int i = 0; i < chord.Length; i++) host.Down(chord[i]);
        Assert.True(host.HookDown(chord[^1]), "the hook must observe the trigger down before WM_HOTKEY");
        bool accepted = host.Trigger(chord[^1]);
        if (accepted && boundaryWhileHeld) host.ApplyGameMode(!host.GameMode);
        for (int i = chord.Length - 1; i >= 0; i--) host.Up(chord[i]);
        if (accepted && !boundaryWhileHeld) host.ApplyGameMode(!host.GameMode);
        return accepted;
    }

    private static uint[] ToggleChord(HostSequence host)
    {
        var binding = host.ToggleBinding;
        var chord = new List<uint>();
        if ((binding.Modifiers & HotkeyCatalog.ModWin) != 0) chord.Add(0x5B);
        if ((binding.Modifiers & HotkeyCatalog.ModControl) != 0) chord.Add(Ctrl);
        if ((binding.Modifiers & HotkeyCatalog.ModAlt) != 0) chord.Add(Alt);
        if ((binding.Modifiers & HotkeyCatalog.ModShift) != 0) chord.Add(0xA0);
        chord.Add(binding.VirtualKey);
        return chord.ToArray();
    }

    private static void PressBlocked(HostSequence host, params uint[] chord)
    {
        int delivered = host.DeliveredHotkeys;
        foreach (uint key in chord) host.Down(key);
        Assert.False(host.Trigger(chord[^1]));
        Assert.Equal(delivered, host.DeliveredHotkeys); // unregistered in game mode: no WM_HOTKEY at all
        for (int i = chord.Length - 1; i >= 0; i--) host.Up(chord[i]);
    }

    [Theory]
    [InlineData(0, false, false)] [InlineData(0, false, true)] [InlineData(0, true, false)] [InlineData(0, true, true)]
    [InlineData(1, false, false)] [InlineData(1, false, true)] [InlineData(1, true, false)] [InlineData(1, true, true)]
    [InlineData(2, false, false)] [InlineData(2, false, true)] [InlineData(2, true, false)] [InlineData(2, true, true)]
    public void FirstToggleAfterBlockedChordIsAccepted(int chord, bool paletteWindowsKey, bool boundaryWhileHeld)
    {
        var host = new HostSequence(paletteWindowsKey);
        Assert.True(PressToggle(host, boundaryWhileHeld)); // Ctrl+Alt+G turns game mode ON
        Assert.True(host.GameMode);
        Assert.Contains(HotkeyCatalog.GameModeToggleId, host.RegisteredIds);
        Assert.Contains(HotkeyCatalog.PaletteId, host.RegisteredIds); // palette binding retained
        switch (chord)
        {
            case 0: Assert.DoesNotContain("snap-left", host.RegisteredIds); PressBlocked(host, Alt, A); break;
            case 1: Assert.DoesNotContain("edge-resize-left", host.RegisteredIds); PressBlocked(host, Ctrl, Alt, Left); break;
            default: PressBlocked(host, Alt); break;
        }
        Assert.True(host.GameMode);
        host.Advance(2000); // the reporter's pause before trying the toggle again
        Assert.Contains(HotkeyCatalog.PaletteId, host.RegisteredIds); // retained through the blocked chord

        Assert.True(PressToggle(host, boundaryWhileHeld),
            "the first Ctrl+Alt+G after the blocked chord must be accepted"); // ON->OFF on the very first press
        Assert.False(host.GameMode);
        Assert.Contains(HotkeyCatalog.PaletteId, host.RegisteredIds);
        Assert.True(PressToggle(host, boundaryWhileHeld)); // OFF->ON again on the first press
        Assert.True(host.GameMode);
    }

    [Fact]
    public void PaletteBindingStillTriggersInGameMode()
    {
        var host = new HostSequence(paletteWindowsKey: false);
        Assert.True(PressToggle(host, boundaryWhileHeld: true));
        Assert.True(host.GameMode);
        PressBlocked(host, Alt, A);
        PressBlocked(host, Ctrl, Alt, Left);
        PressBlocked(host, Alt);
        host.Advance(2000);
        Assert.Contains(HotkeyCatalog.PaletteId, host.RegisteredIds);
        host.Down(0x5B); host.Down(Alt); host.Down(0x20); // Win+Alt+Space
        Assert.True(host.Trigger(0x20));
        host.Up(0x20); host.Up(Alt); host.Up(0x5B);
        Assert.True(host.GameMode);
    }

    [Fact]
    public void RepeatGateIsDownTracksHookObservedHeldKeys()
    {
        var gate = new ShortcutRepeatGate();
        Assert.False(gate.IsDown((int)G));
        gate.Observe(G, true);
        Assert.True(gate.IsDown((int)G));
        gate.Observe(G, false);
        Assert.False(gate.IsDown((int)G));
        gate.Observe(G, true);
        gate.Reset();
        Assert.False(gate.IsDown((int)G));
    }

    [Fact]
    public void HookReleaseClearsIsDownEvenWhenThePairedUpIsSwallowed()
    {
        var host = new HostSequence(paletteWindowsKey: false);
        host.Down(Ctrl); host.Down(Alt); host.Down(G);
        Assert.True(host.Trigger(G)); // no mode change yet: ownership keeps the paired up armed
        host.Up(G);
        Assert.False(host.HookDown(G)); // the hook observed the physical release
        Assert.True(host.AsyncDown(G)); // Windows discarded it, so async state still says down
        host.Up(Alt); host.Up(Ctrl);
    }

    [Fact]
    public void BoundaryHeldRepeatIsBlockedThenFreshDownIsAccepted()
    {
        var host = new HostSequence(paletteWindowsKey: true);
        host.Down(Ctrl); host.Down(Alt); host.Down(G);
        Assert.True(host.Trigger(G));
        Assert.True(host.HookDown(G));
        host.ApplyGameMode(true); // the hook state still holds the chord at the boundary
        Assert.True(host.BoundaryHeld(G));
        host.Down(G); Assert.False(host.Trigger(G)); // held auto-repeat is blocked at the boundary
        Assert.True(host.GameMode);
        host.Up(G); Assert.False(host.HookDown(G));
        host.Down(G); Assert.True(host.Trigger(G)); // fresh press after the observed release
        host.ApplyGameMode(false);
        Assert.False(host.GameMode);
        host.Up(G); host.Up(Alt); host.Up(Ctrl);
    }

    [Fact]
    public void ReboundToggleAcceptsTheFirstPressAfterBlockedChords()
    {
        var host = new HostSequence(paletteWindowsKey: true,
            new Dictionary<string, string> { [HotkeyCatalog.GameModeToggleId] = "Win+Alt+P" });
        Assert.Equal("Win+Alt+P", host.ToggleBinding.Shortcut);
        Assert.True(PressToggle(host, boundaryWhileHeld: false));
        Assert.True(host.GameMode);
        Assert.Contains(HotkeyCatalog.PaletteId, host.RegisteredIds);
        PressBlocked(host, Alt, A);
        PressBlocked(host, Ctrl, Alt, Left);
        PressBlocked(host, Alt);
        Assert.Contains(HotkeyCatalog.PaletteId, host.RegisteredIds);
        host.Advance(2000);
        Assert.True(PressToggle(host, boundaryWhileHeld: false)); // first press turns it off
        Assert.False(host.GameMode);
        Assert.Contains(HotkeyCatalog.PaletteId, host.RegisteredIds);
        Assert.True(PressToggle(host, boundaryWhileHeld: true));
        Assert.True(host.GameMode);
    }

    [Fact]
    public void BoundaryGuardSurvivesASecondModeTransitionBeforeTheKeyup()
    {
        var host = new HostSequence(paletteWindowsKey: true);
        host.Down(Ctrl); host.Down(Alt); host.Down(G);
        Assert.True(host.Trigger(G));
        host.ApplyGameMode(true); // mode ON while G is still held; the gate is reset here
        Assert.True(host.BoundaryHeld(G));
        host.ApplyGameMode(false); // second transition before any repeat or up
        Assert.True(host.BoundaryHeld(G)); // union retains the earlier boundary's held guard
        host.Down(G); Assert.False(host.Trigger(G)); // held repeat is still rejected
        Assert.False(host.GameMode);
        host.Up(G);
        Assert.False(host.BoundaryHeld(G));
        host.Down(G); Assert.True(host.Trigger(G)); // real up, then a fresh down is accepted
        host.ApplyGameMode(true);
        Assert.True(host.GameMode);
        host.Up(G); host.Up(Alt); host.Up(Ctrl);
    }

    [Fact]
    public void LegacyAsyncBoundarySeedLosesFirstToggleButHookSeedDoesNot()
    {
        // Released-before-boundary (fast injected tap): the trigger up was swallowed before
        // the mode change, so only the old async-state seed still calls the key held.
        var legacy = new HostSequence(paletteWindowsKey: false, legacyAsyncBoundary: true);
        Assert.True(PressToggle(legacy, boundaryWhileHeld: false));
        Assert.True(legacy.GameMode);
        Assert.True(legacy.AsyncDown(G));
        Assert.True(legacy.BoundaryHeld(G)); // legacy Seed(IsDown) resurrects the released toggle
        PressBlocked(legacy, Alt, A);
        legacy.Advance(2000);
        Assert.False(PressToggle(legacy, boundaryWhileHeld: false)); // first OFF press is lost
        Assert.True(legacy.GameMode);
        Assert.True(PressToggle(legacy, boundaryWhileHeld: false)); // the next one works
        Assert.False(legacy.GameMode);

        var production = new HostSequence(paletteWindowsKey: false);
        Assert.True(PressToggle(production, boundaryWhileHeld: false));
        Assert.True(production.GameMode);
        Assert.True(production.AsyncDown(G));
        Assert.False(production.BoundaryHeld(G)); // hook seed ignores the stale async state
        PressBlocked(production, Alt, A);
        production.Advance(2000);
        Assert.True(PressToggle(production, boundaryWhileHeld: false)); // first OFF press accepted
        Assert.False(production.GameMode);
    }

    [Fact]
    public void HeldToggleRepeatsNeverRetoggleAcrossTheModeBoundary()
    {
        var host = new HostSequence(paletteWindowsKey: true);
        host.Down(Ctrl); host.Down(Alt); host.Down(G);
        Assert.True(host.Trigger(G));
        host.ApplyGameMode(true); // boundary seeds while the toggle chord is still held
        host.Down(G); Assert.False(host.Trigger(G)); // native auto-repeat must not toggle back
        host.Down(G); Assert.False(host.Trigger(G));
        Assert.True(host.GameMode);
        host.Up(G); host.Up(Alt); host.Up(Ctrl);
    }

    [Fact]
    public void HeldToggleRepeatsAreRejectedByTheRepeatGate()
    {
        var host = new HostSequence(paletteWindowsKey: false);
        host.Down(Ctrl); host.Down(Alt); host.Down(G);
        Assert.True(host.Trigger(G)); // no mode change applied: repeat gate does the work alone
        host.Down(G); Assert.False(host.Trigger(G));
        host.Down(G); Assert.False(host.Trigger(G));
        host.Up(G);
        host.Down(G); Assert.True(host.Trigger(G)); // a fresh hold is accepted again
        host.Up(G); host.Up(Alt); host.Up(Ctrl);
    }
}
