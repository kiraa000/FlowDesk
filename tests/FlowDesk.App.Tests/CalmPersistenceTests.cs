using FlowDesk.App.Services;
using FlowDesk.Core.Calm;

namespace FlowDesk.App.Tests;

public sealed class CalmPersistenceTests
{
    [Fact] public void OldSettingsNeedOneCheckAndCompletedFlagSurvivesRestart()
    {
        Assert.False(AppSettings.FromJson("{}").FirstCalmCheckDone);
        Assert.True(AppSettings.FromJson(new AppSettings { FirstCalmCheckDone = true }.ToJson()).FirstCalmCheckDone);
    }
    [Fact] public void FileJournalSurvivesReplacementAndKeepsPriorBackup()
    {
        // Project build output on D:, never real app settings or the user's registry.
        string folder = Path.Combine(AppContext.BaseDirectory, "calm-test-artifacts", Guid.NewGuid().ToString("N"));
        var store = new FileCalmJournalStore(folder);
        Assert.Empty(store.Load().Rows);
        var journal = new CalmJournal();
        var row = CalmCatalog.All[0];
        journal.Rows.Add(row.Id, [new(row.Values[0].Address, null)]);
        store.Save(journal);
        Assert.Null(new FileCalmJournalStore(folder).Load().Rows[row.Id][0].Previous);
        journal.Rows[row.Id][0] = new(row.Values[0].Address, new RegistryValue(7, "[\"a\",\"b\"]"));
        store.Save(journal);
        Assert.Equal(journal.ToJson(), new FileCalmJournalStore(folder).Load().ToJson());
        Assert.True(File.Exists(Path.Combine(folder, "calm-undo.json.bak")));
        Assert.False(File.Exists(Path.Combine(folder, "calm-undo.json.tmp")));
    }
    [Fact] public void CorruptJournalFailsClosedInsteadOfDiscardingOriginals()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "calm-test-artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "calm-undo.json"), "broken");
        Assert.Throws<System.Text.Json.JsonException>(() => new FileCalmJournalStore(folder).Load());
    }
}
