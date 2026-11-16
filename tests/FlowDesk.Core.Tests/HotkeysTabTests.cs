using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class HotkeysTabTests
{
    private sealed class Registrar : IShortcutRegistrar
    {
        public Dictionary<int, string> Active { get; } = [];
        public Dictionary<string, ShortcutRegistrationResult> Results { get; } = [];
        public int RegisterCalls, UnregisterCalls;
        public bool FailUnregister;
        public ShortcutRegistrationResult Register(int id, HotkeyBinding binding)
        {
            RegisterCalls++;
            var result = Results.GetValueOrDefault(binding.Id, ShortcutRegistrationResult.Active);
            if (result == ShortcutRegistrationResult.Active) Active.Add(id, binding.Id);
            return result;
        }
        public bool Unregister(int id) { UnregisterCalls++; if (FailUnregister) return false; Active.Remove(id); return true; }
    }
    private static ShortcutRuntime Runtime(bool desktops = true, bool mouse = true, bool keyboard = true, bool running = true,
        IEnumerable<string>? registered = null, IEnumerable<string>? conflicts = null) =>
        new(running, desktops, mouse, keyboard, new(new HashSet<string>(registered ?? HotkeyCatalog.Registerable.Select(b => b.Id)), new HashSet<string>(conflicts ?? [])));

    [Fact] public void GroupMappingCoversCatalogueExactlyOnceAndPreservesItsOrder()
    {
        var grouped = ShortcutPresentation.Groups.SelectMany(g => HotkeyCatalog.All.Where(b => ShortcutPresentation.Group(b) == g.Group)).ToArray();
        Assert.Equal(HotkeyCatalog.All.Select(b => b.Id), grouped.Select(b => b.Id));
        Assert.Equal(HotkeyCatalog.All.Count, grouped.Select(b => b.Id).Distinct().Count());
        Assert.Equal(new[] { "Command palette", "Desktops", "Snap", "Focus", "Window commands", "Terminal, reload and pause", "Mouse wheel" }, ShortcutPresentation.Groups.Select(g => g.Title));
    }
    [Fact] public void DisabledIdsAreKnownDistinctOrdinalAndDefensivelyCopied()
    {
        var input = new List<string> { "snap-up", "snap-up", "unknown", "SNAP-UP", null! };
        var policy = new ShortcutPolicy(input); input.Clear();
        Assert.Equal(new[] { "snap-up" }, policy.DisabledIds); Assert.False(policy.Enabled("snap-up"));
        Assert.True(policy.Enabled("snap-left")); Assert.False(policy.Enabled("unknown"));
        Assert.Empty(new ShortcutPolicy(null).DisabledIds);
    }
    [Fact] public void RegistrationSkipsDisabledWheelAndUnavailableDesktopIds()
    {
        var registrar = new Registrar(); var registrations = new ShortcutRegistrations();
        var policy = new ShortcutPolicy(["snap-up", HotkeyCatalog.PauseToggleId, "resize-wheel"]);
        var snapshot = registrations.Apply(policy, false, registrar);
        Assert.Equal(HotkeyCatalog.All.Where(b => policy.CanRegister(b, false)).Select(b => b.Id).Order(), snapshot.Registered.Order());
        Assert.DoesNotContain("snap-up", registrar.Active.Values); Assert.DoesNotContain(HotkeyCatalog.PauseToggleId, registrar.Active.Values);
        Assert.All(registrar.Active.Values, id => Assert.False(HotkeyCatalog.Find(id)!.IsMouseWheel || HotkeyCatalog.Find(id)!.IsDesktop));
    }
    [Fact] public void ModuleOffRegistrationPolicyCannotClaimAnyGlobalKey()
    {
        var registrar = new Registrar(); var registrations = new ShortcutRegistrations();
        var snapshot = registrations.Apply(ShortcutPolicy.AllOff, true, registrar);
        Assert.Empty(snapshot.Registered); Assert.Empty(snapshot.Conflicts); Assert.Equal(0, registrar.RegisterCalls);
        Assert.All(HotkeyCatalog.All, b => Assert.False(ShortcutPolicy.AllOff.Enabled(b.Id)));
    }
    [Fact] public void UnavailableDesktopWheelPassesThroughBeforeOwnershipButOtherWheelActionsRemainUsable()
    {
        var policy = new ShortcutPolicy([]);
        Assert.False(policy.AllowsWheel(WheelGesture.Desktop, false)); Assert.False(policy.AllowsWheel(WheelGesture.MoveDesktop, false));
        Assert.True(policy.AllowsWheel(WheelGesture.Resize, false)); Assert.True(policy.AllowsWheel(WheelGesture.Search, false));
    }
    [Fact] public void DisablingFreesRegistrationAndReenableDoesNotReuseStaleNativeId()
    {
        var registrar = new Registrar(); var registrations = new ShortcutRegistrations();
        registrations.Apply(new([]), true, registrar);
        int oldId = registrar.Active.Single(x => x.Value == "snap-up").Key;
        var otherIds = registrar.Active.Where(x => x.Value != "snap-up").ToArray(); int calls = registrar.RegisterCalls;
        registrations.Apply(new(["snap-up"]), true, registrar);
        Assert.Equal(1, registrar.UnregisterCalls); Assert.False(registrations.TryGet(oldId, out _));
        Assert.Equal(calls, registrar.RegisterCalls);
        registrations.Apply(new([]), true, registrar);
        Assert.NotEqual(oldId, registrar.Active.Single(x => x.Value == "snap-up").Key);
        Assert.All(otherIds, pair => Assert.Equal(pair.Value, registrar.Active[pair.Key]));
    }
    [Fact] public void ConflictsReevaluateAndOffNeverReportsOrClaimsFailedKeys()
    {
        var registrar = new Registrar(); registrar.Results["terminal-open"] = ShortcutRegistrationResult.Conflict;
        var registrations = new ShortcutRegistrations();
        Assert.Contains("terminal-open", registrations.Apply(new([]), true, registrar).Conflicts);
        Assert.DoesNotContain("terminal-open", registrations.Apply(new(["terminal-open"]), true, registrar).Conflicts);
        registrar.Results.Clear();
        Assert.Contains("terminal-open", registrations.Apply(new([]), true, registrar).Registered);
    }
    [Fact] public void FailedUnregisterIsReportedAndCanBeRetriedWithoutLosingOwnershipBookkeeping()
    {
        var registrar = new Registrar(); var registrations = new ShortcutRegistrations();
        registrations.Apply(new([]), true, registrar); int id = registrar.Active.Single(x => x.Value == "minimize").Key;
        registrar.FailUnregister = true;
        Assert.Throws<InvalidOperationException>(() => registrations.Apply(new(["minimize"]), true, registrar));
        Assert.True(registrations.TryGet(id, out _));
        registrar.FailUnregister = false; registrations.Apply(new(["minimize"]), true, registrar);
        Assert.False(registrations.TryGet(id, out _));
    }
    [Fact] public void QueuedWorkCannotSurviveOffOnPolicyChange()
    {
        var old = new ShortcutPolicy([]); var disabled = new ShortcutPolicy(["snap-up"]); var reenabled = new ShortcutPolicy([]);
        Assert.True(old.CanExecute(old, "snap-up")); Assert.False(old.CanExecute(disabled, "snap-up"));
        Assert.False(old.CanExecute(reenabled, "snap-up")); Assert.True(reenabled.CanExecute(reenabled, "snap-up"));
    }
    [Theory]
    [InlineData(WheelGesture.Resize, "resize-wheel")] [InlineData(WheelGesture.Desktop, "desktop-wheel")]
    [InlineData(WheelGesture.MoveDesktop, "desktop-move-wheel")] [InlineData(WheelGesture.Search, "search-wheel")]
    public void WheelFilterRunsBeforeAcceptanceOrSuppression(WheelGesture gesture, string id)
    {
        var policy = new ShortcutPolicy([id]); Assert.False(policy.AllowsWheel(gesture));
        Assert.True(new ShortcutPolicy([]).AllowsWheel(gesture)); Assert.Equal(id, ShortcutPolicy.WheelId(gesture));
        Assert.False(policy.AllowsWheel(WheelGesture.None));
    }
    [Fact] public void StatusPriorityOffConflictUnavailableAndActive()
    {
        var binding = HotkeyCatalog.Find("terminal-open")!; var policy = new ShortcutPolicy([]);
        Assert.Equal(ShortcutStatus.Active, ShortcutPresentation.Status(binding, true, policy, Runtime()));
        Assert.Equal(ShortcutStatus.Off, ShortcutPresentation.Status(binding, false, policy, Runtime()));
        Assert.Equal(ShortcutStatus.Off, ShortcutPresentation.Status(binding, true, new([binding.Id]), Runtime(conflicts: [binding.Id])));
        Assert.Equal(ShortcutStatus.Conflict, ShortcutPresentation.Status(binding, true, policy, Runtime(conflicts: [binding.Id])));
        Assert.Equal(ShortcutStatus.Unavailable, ShortcutPresentation.Status(binding, true, policy, Runtime(registered: [])));
        Assert.Equal(ShortcutStatus.Unavailable, ShortcutPresentation.Status(binding, true, policy, Runtime(running: false)));
    }
    [Theory] [InlineData("desktop-switch-1")] [InlineData("desktop-wheel")] [InlineData("desktop-move-wheel")]
    public void DesktopActionsNeedDesktopBackend(string id) => Assert.Equal(ShortcutStatus.Unavailable,
        ShortcutPresentation.Status(HotkeyCatalog.Find(id)!, true, new([]), Runtime(desktops: false)));
    [Fact] public void WheelAvailabilityDistinguishesMouseAndKeyboardHooks()
    {
        Assert.Equal(ShortcutStatus.Active, ShortcutPresentation.Status(HotkeyCatalog.Find("resize-wheel")!, true, new([]), Runtime(keyboard: false)));
        Assert.Equal(ShortcutStatus.Unavailable, ShortcutPresentation.Status(HotkeyCatalog.Find("desktop-wheel")!, true, new([]), Runtime(keyboard: false)));
        Assert.Equal(ShortcutStatus.Unavailable, ShortcutPresentation.Status(HotkeyCatalog.Find("resize-wheel")!, true, new([]), Runtime(mouse: false)));
    }
    [Theory] [InlineData(" alt+enter ", true)] [InlineData("PROFILE FOLDER", true)] [InlineData("nothing matches", false)] [InlineData("", true)]
    public void FilterUsesBothLabelAndShortcut(string query, bool expected) => Assert.Equal(expected, ShortcutPresentation.Matches(HotkeyCatalog.Find("terminal-open")!, query));
    [Fact] public void OptionalNavigationIsReusableAndCannotVisitHiddenTab()
    {
        var rules = new Dictionary<string, bool> { ["Hotkeys"] = false, ["Advanced"] = false };
        Assert.False(OptionalNavigation.Visible("Hotkeys", rules));
        Assert.Equal("Workspaces", OptionalNavigation.Resolve("Hotkeys", "Workspaces", rules));
        Assert.Equal("Settings", OptionalNavigation.Resolve("Settings", "Workspaces", rules));
        rules["Hotkeys"] = true; Assert.True(OptionalNavigation.Visible("Hotkeys", rules));
        Assert.Equal("Hotkeys", OptionalNavigation.Resolve("Hotkeys", "Workspaces", rules));
        Assert.False(OptionalNavigation.Visible("Advanced", rules));
    }
    private sealed class Scheduler : IOneShotScheduler
    {
        public List<Action> Callbacks { get; } = [];
        public IDisposable Schedule(TimeSpan delay, Action callback) { Callbacks.Add(callback); return new Empty(); }
        private sealed class Empty : IDisposable { public void Dispose() { } }
    }
    [Fact] public void MultiPressContextIsCapturedBeforeResetOrNextPress()
    {
        var scheduler = new Scheduler(); var queue = new MultiPressQueue(scheduler); var old = new object(); var next = new object();
        object? observed = null;
        queue.Fired += (_, _) => { queue.Reset(); queue.Press(WindowDirection.Down, next); };
        queue.FiredWithContext += (_, _, context) => observed = context;
        queue.Press(WindowDirection.Up, old); scheduler.Callbacks[0]();
        Assert.Same(old, observed);
    }
}
