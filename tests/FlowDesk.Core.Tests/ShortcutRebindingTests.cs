using System.Globalization;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class ShortcutRebindingTests
{
    private static KeyChord Chord(string text) { Assert.True(KeyChord.TryParse(text, out var c)); return c; }
    [Theory]
    [InlineData("Win+Ctrl+Alt+Shift+Space")]
    [InlineData("Ctrl+A")]
    [InlineData("Alt+Enter")]
    [InlineData("F13")]
    [InlineData("F24")]
    [InlineData("Win+.")]
    [InlineData("Ctrl+VKBA")]
    [InlineData("Shift+Num4")]
    public void ChordsRoundTripCultureIndependently(string text)
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); Assert.Equal(text, Chord(text).ToString()); Assert.Equal(Chord(text), Chord(Chord(text).ToString())); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact] public void EveryEditableDefaultParsesAndValidates()
    {
        foreach (var b in HotkeyCatalog.Registerable)
        { Assert.Equal(KeyChord.From(b), Chord(b.Shortcut)); Assert.Null(Chord(b.Shortcut).ValidationError); }
    }
    [Theory]
    [InlineData("Ctrl+Ctrl+A")][InlineData("Ctrl+A+B")][InlineData("Ctrl+Wheel")][InlineData("Ctrl+")]
    [InlineData("Win+VKFF")][InlineData("F25")][InlineData("")][InlineData("Ctrl+VK10")]
    public void ParserRejectsMalformedOrUnsupportedKeys(string text) => Assert.False(KeyChord.TryParse(text, out _));
    [Theory]
    [InlineData("Esc")][InlineData("Tab")][InlineData("Enter")][InlineData("Space")][InlineData("A")][InlineData("F1")]
    public void BareOrdinaryKeysAreInvalid(string text) => Assert.NotNull(Chord(text).ValidationError);
    [Fact] public void LoneModifiersUnsupportedAndUnknownBitsAreInvalid()
    {
        Assert.NotNull(new KeyChord(2, 0).ValidationError);
        Assert.NotNull(new KeyChord(0, 0x11).ValidationError);
        Assert.NotNull(new KeyChord(16, 0x41).ValidationError);
        Assert.NotNull(new KeyChord(2, 1).ValidationError);
    }
    [Theory]
    [InlineData("Win+L")][InlineData("Ctrl+Alt+Delete")][InlineData("Alt+Tab")][InlineData("Ctrl+Shift+Esc")][InlineData("Ctrl+F12")]
    public void ReservedWindowsShortcutsBlock(string text) => Assert.True(ShortcutRebinding.Evaluate(HotkeyCatalog.All, "center", Chord(text)).Blocked);
    [Theory]
    [InlineData("Win+D")][InlineData("Win+E")][InlineData("Win+R")][InlineData("Win+I")][InlineData("Win+Tab")]
    [InlineData("Win+V")][InlineData("Win+.")][InlineData("Win+Shift+S")][InlineData("Alt+F4")][InlineData("Win+0")][InlineData("Win+9")][InlineData("Win+Left")]
    public void ReplaceableWindowsShortcutsOnlyWarn(string text)
    {
        var result = ShortcutRebinding.Evaluate(HotkeyCatalog.All, "center", Chord(text));
        Assert.False(result.Blocked); Assert.Equal(WindowsShortcutKind.Warning, result.Windows?.Kind);
    }
    [Fact] public void WindowsTableHasNoDuplicateChordsAndEveryEntryRoundTrips()
    {
        Assert.Equal(WindowsShortcuts.All.Count, WindowsShortcuts.All.Select(x => x.Chord).Distinct().Count());
        Assert.All(WindowsShortcuts.All, x => Assert.Equal(x.Chord, Chord(x.Chord.ToString())));
    }
    [Fact] public void EffectiveMergeDropsDefaultUnknownWheelInvalidAndReservedEntries()
    {
        var issues = new List<string>();
        var normalized = ShortcutRebinding.Normalize(new Dictionary<string, string> {
            ["center"] = "ctrl+alt+j", ["minimize"] = "Alt+M", ["unknown"] = "Ctrl+J", ["resize-wheel"] = "Ctrl+J",
            ["maximize-toggle"] = "bad", ["terminal-open"] = "Win+L" }, issues.Add);
        Assert.Single(normalized); Assert.Equal("Ctrl+Alt+J", normalized["center"]); Assert.Equal(4, issues.Count);
        Assert.Equal(Chord("Ctrl+Alt+J"), KeyChord.From(ShortcutRebinding.Effective(normalized).Single(b => b.Id == "center")));
        Assert.Equal(HotkeyCatalog.Find("minimize"), ShortcutRebinding.Effective(normalized).Single(b => b.Id == "minimize"));
    }
    [Fact] public void ConflictIncludesPaletteAndClipboardButNotSelfOrWheel()
    {
        Assert.Equal(HotkeyCatalog.PaletteId, Assert.Single(ShortcutRebinding.Evaluate(HotkeyCatalog.All, "center", KeyChord.From(HotkeyCatalog.Find(HotkeyCatalog.PaletteId)!)).Others).Id);
        Assert.Equal(HotkeyCatalog.ClipboardId, Assert.Single(ShortcutRebinding.Evaluate(HotkeyCatalog.All, "center", KeyChord.From(HotkeyCatalog.Find(HotkeyCatalog.ClipboardId)!)).Others).Id);
        Assert.Empty(ShortcutRebinding.Evaluate(HotkeyCatalog.All, "center", Chord("Alt+Shift+Space")).Others);
        Assert.Contains("Quick Look", ShortcutRebinding.Evaluate(HotkeyCatalog.All, "center", new(0, 0x20), true).Error);
    }
    [Fact] public void InternalConflictRequiresResolutionAndSwapPreservesOtherOffChoice()
    {
        Assert.Throws<InvalidOperationException>(() => ShortcutRebinding.Plan(null, [], "center", Chord("Alt+M"), ShortcutResolution.None));
        var plan = ShortcutRebinding.Plan(null, ["minimize"], "center", Chord("Alt+M"), ShortcutResolution.Swap);
        Assert.Equal("Alt+M", plan.Overrides["center"]); Assert.Equal("Alt+Shift+Space", plan.Overrides["minimize"]);
        Assert.Contains("minimize", plan.DisabledIds);
    }
    [Fact] public void TurnOffOtherPreservesItsKeysAndRevertingToDefaultRemovesOverride()
    {
        var plan = ShortcutRebinding.Plan(null, [], "center", Chord("Alt+M"), ShortcutResolution.TurnOtherOff);
        Assert.Contains("minimize", plan.DisabledIds); Assert.False(plan.Overrides.ContainsKey("minimize"));
        var reset = ShortcutRebinding.Plan(plan.Overrides, plan.DisabledIds, "center", KeyChord.From(HotkeyCatalog.Find("center")!), ShortcutResolution.None);
        Assert.Empty(reset.Overrides); Assert.Contains("minimize", reset.DisabledIds);
    }
    [Fact] public void SwapCannotCreateAThirdEnabledCollision()
    {
        var overrides = new Dictionary<string, string> { ["center"] = "Alt+M" };
        Assert.Throws<InvalidOperationException>(() => ShortcutRebinding.Plan(overrides, [], "center", Chord("Alt+F"), ShortcutResolution.Swap));
    }
    [Fact] public void CaptureKeepsCandidateAfterReleaseAndNeverAcceptsTwoActionKeys()
    {
        var state = new ShortcutCapture(); state.Observe(0x5B, true); state.Observe(0x4A, true);
        Assert.Equal(new KeyChord(8, 0x4A), state.Candidate);
        state.Observe(0x4B, true); Assert.True(state.MultipleKeys); Assert.Null(state.Candidate);
        state.ResetHeld(); state.Observe(0x11, true); state.Observe(0x4A, true); state.Observe(0x4A, false); state.Observe(0x11, false);
        Assert.Equal(new KeyChord(2, 0x4A), state.Candidate); Assert.Equal(default, state.Held);
    }
    [Fact] public void CaptureLoneModifierInvalidatesPreviousCandidateUntilAKeyIsPressed()
    {
        var state = new ShortcutCapture(); state.Observe(0x11, true); state.Observe(0x4A, true);
        state.ResetHeld(); state.Observe(0x5B, true);
        Assert.Equal(new KeyChord(8, 0), state.Candidate); Assert.NotNull(state.Candidate.GetValueOrDefault().ValidationError);
        state.Observe(0x4A, true); state.Observe(0x4B, true); state.Observe(0x4B, false);
        Assert.False(state.MultipleKeys); Assert.Null(state.Candidate);
    }
    [Fact] public void RepeatRuleBelongsToActionAfterOwnershipTransferNotItsOldNativeChord()
    {
        var gate = new ShortcutRepeatGate();
        var snap = HotkeyCatalog.Find("snap-up")!;
        var center = HotkeyCatalog.Find("center")! with { Modifiers = snap.Modifiers, VirtualKey = snap.VirtualKey };
        gate.Observe(snap.VirtualKey, true);
        Assert.True(gate.Accept(snap)); Assert.True(gate.Accept(snap));
        Assert.True(gate.Accept(center)); Assert.False(gate.Accept(center));
        gate.Observe(snap.VirtualKey, true); Assert.False(gate.Accept(center));
        gate.Observe(snap.VirtualKey, false); gate.Observe(snap.VirtualKey, true); Assert.True(gate.Accept(center));
        Assert.False(gate.Accept(center)); gate.Reset(); Assert.True(gate.Accept(center));
    }
    private sealed class Registrar : IShortcutRegistrar
    {
        public List<string> Events { get; } = [];
        public Dictionary<int, KeyChord> Active { get; } = [];
        public KeyChord? Fail { get; set; }
        public ShortcutRegistrationResult Register(int id, HotkeyBinding binding)
        {
            var chord = KeyChord.From(binding); Events.Add("register:" + chord);
            if (chord == Fail || Active.Values.Contains(chord)) return ShortcutRegistrationResult.Conflict;
            Active.Add(id, chord); return ShortcutRegistrationResult.Active;
        }
        public bool Unregister(int id) { Events.Add("unregister:" + Active[id]); return Active.Remove(id); }
    }
    [Fact] public void NewRegistrationPrecedesReleaseAndFailureKeepsOldRegistration()
    {
        var registrar = new Registrar(); var registrations = new ShortcutRegistrations(); var old = HotkeyCatalog.Find("center")!;
        var policy = new ShortcutPolicy(HotkeyCatalog.All.Where(b => b.Id != old.Id).Select(b => b.Id));
        registrations.Apply(policy, true, registrar); int oldId = Assert.Single(registrations.NativeIds);
        var next = old with { Modifiers = 2, VirtualKey = 0x4A, Shortcut = "Ctrl+J" };
        registrar.Fail = KeyChord.From(next); registrar.Events.Clear();
        Assert.Throws<InvalidOperationException>(() => registrations.Apply(policy, true, registrar, [next], true));
        Assert.True(registrations.TryGet(oldId, out var kept)); Assert.Equal(old, kept); Assert.Single(registrar.Active);
        Assert.DoesNotContain(registrar.Events, e => e.StartsWith("unregister:"));
        registrar.Fail = null; registrar.Events.Clear(); registrations.Apply(policy, true, registrar, [next], true);
        Assert.Equal(new[] { "register:Ctrl+J", "unregister:Alt+Shift+Space" }, registrar.Events); Assert.False(registrations.TryGet(oldId, out _));
    }
    [Fact] public void SwapTransfersHeldKeysWithoutReleasingOrProbingThem()
    {
        var a = HotkeyCatalog.Find("center")!; var b = HotkeyCatalog.Find("minimize")!;
        var registrar = new Registrar(); var registrations = new ShortcutRegistrations(); var policy = new ShortcutPolicy([]);
        registrations.Apply(policy, true, registrar, [a, b]); registrar.Events.Clear();
        var next = ShortcutRebinding.Plan(null, [], a.Id, KeyChord.From(b), ShortcutResolution.Swap);
        var snapshot = registrations.Apply(policy, true, registrar, ShortcutRebinding.Effective(next.Overrides).Where(x => x.Id == a.Id || x.Id == b.Id).ToArray(), true);
        Assert.Empty(registrar.Events); Assert.Equal(2, snapshot.Registered.Count);
        Assert.All(registrations.NativeIds, id => { Assert.True(registrations.TryGet(id, out var binding)); Assert.Equal(registrar.Active[id], KeyChord.From(binding)); });
    }
    [Fact] public void TurnAllOnCannotRegisterDuplicateChordsFromDisabledOverride()
    {
        var registrar = new Registrar(); var registrations = new ShortcutRegistrations();
        var table = ShortcutRebinding.Effective(new Dictionary<string, string> { ["center"] = "Alt+M" });
        var snapshot = registrations.Apply(new([]), true, registrar, table);
        Assert.Single(snapshot.Conflicts); Assert.Equal(registrar.Active.Count, registrar.Active.Values.Distinct().Count());
    }
    [Fact] public void FailureOnSecondNewKeyRollsBackFirstAcquisitionWithoutReleasingAnyOldKey()
    {
        var registrar = new Registrar(); var registrations = new ShortcutRegistrations();
        var a = HotkeyCatalog.Find("center")!; var b = HotkeyCatalog.Find("minimize")!;
        registrations.Apply(new([]), true, registrar, [a, b]); var oldIds = registrations.NativeIds.ToArray();
        var nextA = a with { Modifiers = 2, VirtualKey = 0x4A }; var nextB = b with { Modifiers = 2, VirtualKey = 0x4B };
        registrar.Fail = KeyChord.From(nextB); registrar.Events.Clear();
        Assert.Throws<InvalidOperationException>(() => registrations.Apply(new([]), true, registrar, [nextA, nextB], true));
        Assert.Equal(oldIds, registrations.NativeIds); Assert.Equal(2, registrar.Active.Count);
        Assert.Equal(new[] { "register:Ctrl+J", "register:Ctrl+K", "unregister:Ctrl+J" }, registrar.Events);
    }
    [Fact] public void FixedHotCornerOutputsAreProtectedOnlyWhenUsed()
    {
        Assert.Contains("Hot corners", ShortcutRebinding.Evaluate(HotkeyCatalog.All, "center", Chord("Win+D"), hotCornerDesktop: true).Error);
        Assert.True(ShortcutRebinding.Evaluate(HotkeyCatalog.All, "center", Chord("Win+Tab"), hotCornerTaskView: true).Blocked);
        Assert.False(ShortcutRebinding.Evaluate(HotkeyCatalog.All, "center", Chord("Win+D")).Blocked);
        Assert.False(ShortcutRebinding.Evaluate(HotkeyCatalog.All, "center", Chord("Win+Tab")).Blocked);
    }
    [Fact] public void PauseHelpUsesEffectiveResumeKeys()
    {
        var table = ShortcutRebinding.Effective(new Dictionary<string, string> { [HotkeyCatalog.PauseToggleId] = "Ctrl+Alt+J" });
        Assert.Contains("Ctrl+Alt+J", table.Single(b => b.Id == "pause").Label);
        Assert.DoesNotContain(HotkeyCatalog.Find(HotkeyCatalog.PauseToggleId)!.Shortcut, table.Single(b => b.Id == "pause").Label);
    }
}
