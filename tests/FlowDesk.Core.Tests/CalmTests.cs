using FlowDesk.Core.Calm;

namespace FlowDesk.Core.Tests;

public class CalmTests
{
    public static IEnumerable<object[]> Rows => CalmCatalog.All.Select(s => new object[] { s.Id });
    private static CalmSetting Row(string id) => CalmCatalog.All.Single(s => s.Id == id);
    [Theory, MemberData(nameof(Rows))]
    public void EachRowReadsAppliesAndRestoresExactMissingState(string id)
    {
        var row = Row(id); var registry = new FakeRegistry(); var store = new MemoryJournal(); var changes = new CalmChanges(registry, store);
        var reading = CalmEvaluator.Evaluate(row, registry.Read);
        Assert.Equal(row.Values.Any(v => v.Default is null) ? CalmState.Custom : CalmState.Noisy, reading.State);
        Assert.True(reading.UsesDefault);
        changes.Apply(row, true);
        Assert.Equal(CalmState.Calm, CalmEvaluator.Evaluate(row, registry.Read).State);
        Assert.True(changes.HasUndo(id));
        // Simulated app restart uses only the persisted JSON, not the previous engine's memory.
        var restarted = new CalmChanges(registry, store);
        restarted.Undo(id);
        Assert.All(row.Values, v => Assert.Null(registry.Read(v.Address)));
        Assert.False(restarted.HasAnyUndo);
    }
    [Theory, MemberData(nameof(Rows))]
    public void EachRowPreservesPresentTypesAndFirstOriginalAcrossRepeatedChanges(string id)
    {
        var row = Row(id); var registry = new FakeRegistry(); var store = new MemoryJournal(); var changes = new CalmChanges(registry, store);
        foreach (var v in row.Values) registry.Write(v.Address, RegistryValue.Dword(v.On));
        Assert.True(CalmEvaluator.Evaluate(row, registry.Read).IsFinding);
        changes.Apply(row, true); changes.Apply(row, true); changes.Apply(row, false);
        Assert.All(row.Values, v => Assert.Equal(RegistryValue.Dword(v.On), registry.Read(v.Address)));
        Assert.False(changes.HasAnyUndo);
        var unusual = new RegistryValue(2, "\"%USERPROFILE%\\\\odd\"");
        registry.Write(row.Values[0].Address, unusual);
        Assert.Equal(CalmState.Custom, CalmEvaluator.Evaluate(row, registry.Read).State);
        changes.Apply(row, true); changes.Undo(id);
        Assert.Equal(unusual, registry.Read(row.Values[0].Address));
    }
    [Theory, MemberData(nameof(Rows))]
    public void EachRowUnexpectedValueIsNotFindingAndOnWithoutJournalIsReversible(string id)
    {
        var row = Row(id); var registry = new FakeRegistry(); var changes = new CalmChanges(registry, new MemoryJournal());
        foreach (var v in row.Values) registry.Write(v.Address, RegistryValue.Dword(99));
        Assert.False(CalmEvaluator.Evaluate(row, registry.Read).IsFinding);
        changes.Apply(row, false);
        Assert.True(CalmEvaluator.Evaluate(row, registry.Read).IsFinding);
        changes.Undo(id);
        Assert.All(row.Values, v => Assert.Equal(RegistryValue.Dword(99), registry.Read(v.Address)));
    }
    [Fact] public void MixedKnownPairIsNoisyButMixedCustomIsNot()
    {
        var row = Row("windows-tips"); var r = new FakeRegistry();
        r.Write(row.Values[0].Address, RegistryValue.Dword(0));
        Assert.True(CalmEvaluator.Evaluate(row, r.Read).IsFinding);
        r.Write(row.Values[1].Address, RegistryValue.Dword(9));
        Assert.False(CalmEvaluator.Evaluate(row, r.Read).IsFinding);
    }
    [Theory]
    [InlineData(0, CalmState.Calm)] [InlineData(1, CalmState.Noisy)] [InlineData(2, CalmState.Noisy)] [InlineData(3, CalmState.Custom)]
    public void SearchThreeWay(int value, CalmState state)
    {
        var row = Row("taskbar-search"); var r = new FakeRegistry(); r.Write(row.Values[0].Address, RegistryValue.Dword(value));
        Assert.Equal(state, CalmEvaluator.Evaluate(row, r.Read).State);
        var changes = new CalmChanges(r, new MemoryJournal());
        changes.Apply(row, true); changes.Apply(row, false, 1);
        Assert.Equal(1, r.Read(row.Values[0].Address)?.AsDword());
        changes.Undo(row.Id); Assert.Equal(value, r.Read(row.Values[0].Address)?.AsDword());
    }
    [Fact] public void WriteAheadFailureDoesNotTouchRegistry()
    {
        var r = new FakeRegistry(); var changes = new CalmChanges(r, new MemoryJournal { Fail = true });
        Assert.Throws<IOException>(() => changes.Apply(Row("windows-tips"), true));
        Assert.Empty(r.Values);
    }
    [Fact] public void PartialWriteIsRecoverableAfterRestart()
    {
        var r = new FakeRegistry { FailWriteNumber = 2 }; var store = new MemoryJournal(); var changes = new CalmChanges(r, store);
        Assert.Throws<IOException>(() => changes.Apply(Row("windows-tips"), true));
        Assert.Single(r.Values);
        new CalmChanges(r, store).UndoAll(); Assert.Empty(r.Values);
    }
    [Fact] public void FailedUndoJournalSaveRetainsOriginalsForRetry()
    {
        var r = new FakeRegistry(); var store = new MemoryJournal(); var changes = new CalmChanges(r, store);
        changes.Apply(Row("windows-tips"), true); store.Fail = true;
        Assert.Throws<AggregateException>(() => changes.UndoAll()); Assert.True(changes.HasAnyUndo);
        store.Fail = false; changes.UndoAll(); Assert.False(changes.HasAnyUndo);
    }
    [Theory] [InlineData(26100,7308,false)] [InlineData(26100,7309,true)] [InlineData(26200,7309,true)] [InlineData(26300,0,true)] [InlineData(26000,9999,false)]
    public void BuildGate(int build, int revision, bool available)
    {
        var readings = CalmEvaluator.Scan(new FakeRegistry(), new(build, revision));
        Assert.Equal(available, readings.Any(r => r.Setting.BuildGated));
    }
    [Fact] public void ManagedPolicyCannotBeChangedOrCounted()
    {
        var r = new FakeRegistry(); r.Write(new(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", true), RegistryValue.Dword(0));
        var row = Row("taskbar-widgets");
        Assert.Equal(CalmState.Managed, CalmEvaluator.Scan(r, new(26300, 0)).Single(x => x.Setting.Id == row.Id).State);
        Assert.Throws<InvalidOperationException>(() => new CalmChanges(r, new MemoryJournal()).Apply(row, true));
        Assert.Single(r.Values);
    }
    [Fact] public void CalmAllExcludesStartupAndCustom()
    {
        var r = new FakeRegistry(); var store = new MemoryJournal(); var changes = new CalmChanges(r, store);
        var startup = new RegistryAddress(StartupCatalog.Approved + "Run", "App");
        changes.CalmAll(CalmEvaluator.Scan(r, new(26300, 0)));
        Assert.Null(r.Read(startup)); Assert.Null(r.Read(Row("taskbar-search").Values[0].Address));
        changes.UndoAll(); Assert.Empty(r.Values);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void StartupCodecPreservesTimestamp(bool enabled)
    {
        byte[] bytes = [2, 0, 0, 0, 11, 12, 13, 14, 15, 16, 17, 18];
        var result = StartupApprovedCodec.Encode(enabled, RegistryValue.Binary(bytes));
        Assert.Equal(enabled, StartupApprovedCodec.Enabled(result));
        Assert.Equal(bytes[4..], result.AsBinary()![4..]);
        Assert.True(StartupApprovedCodec.Enabled(null));
        Assert.Null(StartupApprovedCodec.Enabled(RegistryValue.Binary([7, 0])));
    }
    [Fact] public void StartupNeverDeletesSourceAndRestoresAbsence()
    {
        var r = new FakeRegistry(); var command = new RegistryValue(2, "\"%APPDATA%\\\\app.exe\"");
        r.Write(new(StartupCatalog.Run, "App"), command);
        var entries = StartupCatalog.Scan(r, new FakeFolder()); Assert.Equal(2, entries.Count); Assert.All(entries, e => Assert.True(e.Enabled));
        var store = new MemoryJournal(); var changes = new CalmChanges(r, store);
        foreach (var e in entries) { changes.SetStartup(e, false); Assert.False(StartupApprovedCodec.Enabled(r.Read(e.Approval))); }
        new CalmChanges(r, store).UndoAll();
        Assert.Equal(command, r.Read(new(StartupCatalog.Run, "App"))); Assert.Single(r.Values);
    }
    [Fact] public void TamperedJournalCannotWriteArbitraryTargets()
    {
        var store = new MemoryJournal(); store.Save(new CalmJournal { Rows = { ["windows-tips"] = [new(new("Unrelated", "Value"), RegistryValue.Dword(7))] } });
        var r = new FakeRegistry(); Assert.Throws<InvalidDataException>(() => new CalmChanges(r, store).UndoAll()); Assert.Empty(r.Values);
    }
    [Fact] public void PolicyIntroducedAfterChangeBlocksUndoAndRetainsJournal()
    {
        var r = new FakeRegistry(); var changes = new CalmChanges(r, new MemoryJournal()); var row = Row("taskbar-widgets");
        changes.Apply(row, true);
        r.Write(new(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", true), RegistryValue.Dword(0));
        Assert.Throws<InvalidOperationException>(() => changes.Undo(row.Id));
        Assert.True(changes.HasUndo(row.Id)); Assert.Equal(0, r.Read(row.Values[0].Address)?.AsDword());
    }
    [Fact] public void InvalidBinaryStartupValueIsCustomNotAnException()
    {
        Assert.Null(StartupApprovedCodec.Enabled(new(3, "\"not-base64!\"")));
        Assert.Throws<InvalidOperationException>(() => StartupApprovedCodec.Encode(false, RegistryValue.Dword(2)));
        Assert.Null(StartupApprovedCodec.Enabled(RegistryValue.Binary([2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])));
    }
    [Fact] public void UndoAllRestoresOtherRowsEvenIfOneBecomesManaged()
    {
        var r = new FakeRegistry(); var changes = new CalmChanges(r, new MemoryJournal());
        changes.Apply(Row("taskbar-widgets"), true); changes.Apply(Row("windows-tips"), true);
        r.Write(new(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", true), RegistryValue.Dword(0));
        Assert.Throws<AggregateException>(() => changes.UndoAll());
        Assert.True(changes.HasUndo("taskbar-widgets")); Assert.False(changes.HasUndo("windows-tips"));
        Assert.All(Row("windows-tips").Values, v => Assert.Null(r.Read(v.Address)));
    }
    [Fact] public void StartupOnUsesOriginalBeforeSyntheticEnabledValue()
    {
        var r = new FakeRegistry(); var changes = new CalmChanges(r, new MemoryJournal());
        var entry = new StartupEntry("startup-run:App", "App", "app.exe", new(StartupCatalog.Approved + "Run", "App"), true);
        changes.SetStartup(entry, false); changes.SetStartup(entry, true);
        Assert.Null(r.Read(entry.Approval)); Assert.False(changes.HasAnyUndo);
    }
    private sealed class FakeFolder : IStartupFolder { public IReadOnlyList<StartupFile> Entries() => [new("App.lnk", "X:\\Startup\\App.lnk")]; }
    private sealed class FakeRegistry : IRegistryAccess
    {
        public Dictionary<RegistryAddress, RegistryValue> Values { get; } = [];
        public int FailWriteNumber; private int writes;
        public RegistryValue? Read(RegistryAddress a) => Values.GetValueOrDefault(a);
        public void Write(RegistryAddress a, RegistryValue v) { if (++writes == FailWriteNumber) throw new IOException(); Values[a] = v; }
        public void Delete(RegistryAddress a) => Values.Remove(a);
        public IReadOnlyList<string> ValueNames(string path) => Values.Keys.Where(a => a.Path == path).Select(a => a.Name).ToArray();
    }
    private sealed class MemoryJournal : ICalmJournalStore
    {
        private string json = new CalmJournal().ToJson(); public bool Fail;
        public CalmJournal Load() => CalmJournal.FromJson(json);
        public void Save(CalmJournal journal) { if (Fail) throw new IOException(); json = journal.ToJson(); }
    }
}
