using FlowDesk.Core.Storage;

namespace FlowDesk.Core.Tests;

public sealed class StorageRecoveryTests : IDisposable
{
    private readonly string folder = Path.Combine(@"D:\Workspace\_tmp\opencode", "recovery-" + Guid.NewGuid().ToString("N"));
    public StorageRecoveryTests() => Directory.CreateDirectory(folder);
    public void Dispose() => Directory.Delete(folder, true);

    [Fact] public async Task CorruptMainIsPreservedAndBackupLoaded()
    {
        var store = new JsonStore(folder);
        File.WriteAllText(store.FilePath, "broken-original");
        File.WriteAllText(store.FilePath + ".bak", "{\"TourCompleted\":true}");
        Assert.True(store.Load(out var recovered).TourCompleted);
        Assert.Equal("broken-original", File.ReadAllText(recovered!));
        await store.SaveAsync(new());
        Assert.Equal("broken-original", File.ReadAllText(recovered!));
    }
    [Fact] public async Task LockedMainFallsBackAndCannotBeOverwrittenEvenAfterUnlock()
    {
        var store = new JsonStore(folder);
        File.WriteAllText(store.FilePath, "only-original");
        File.WriteAllText(store.FilePath + ".bak", "{\"TourCompleted\":true}");
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(store.Load(out var recovered).TourCompleted);
            Assert.Equal(store.FilePath, recovered);
        }
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(new()));
        Assert.Equal("only-original", File.ReadAllText(store.FilePath));
    }
    [Fact] public void DeniedReadStillTriesBackupAndDisablesSaveIfAsideFails()
    {
        string path = Path.Combine(folder, "missing.json");
        var file = new RecoverableJsonFile(path);
        int calls = 0;
        string result = file.Load(p => { calls++; if (p == path) throw new UnauthorizedAccessException(); return "backup"; }, () => "default", out var recovered);
        Assert.Equal("backup", result); Assert.Equal(2, calls); Assert.Equal(path, recovered);
        Assert.False(file.CanSave); Assert.Throws<IOException>(file.EnsureCanSave);
    }
    [Theory]
    [InlineData("{\"Workspaces\":null,\"Focus\":null}")]
    [InlineData("{\"Workspaces\":[null,{\"Items\":null}],\"Focus\":{\"Allowed\":null,\"Blocked\":null}}")]
    [InlineData("{\"Workspaces\":[{\"Items\":[null,{\"Urls\":null}]}]}")]
    public void NullObjectsAndCollectionsAreNormalized(string json)
    {
        var store = new JsonStore(folder); File.WriteAllText(store.FilePath, json);
        var data = store.Load(out var recovered);
        Assert.Null(recovered); Assert.NotNull(data.Focus); Assert.NotNull(data.Focus.Allowed); Assert.NotNull(data.Focus.Blocked);
        Assert.All(data.Workspaces, w => Assert.All(w.Items, i => Assert.NotNull(i.Urls)));
    }
}
