using FlowDesk.App.Services;
using FlowDesk.Core.Storage;

namespace FlowDesk.App.Tests;

public sealed class AdversarialStorageTests
{
    [Fact] public void CancellationAndUndoGuardsAreWiredToPersistence()
    {
        string service = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", "Services", "AppServices.cs"));
        Assert.Contains("catch (OperationCanceledException) { LastSaveSucceeded = false; return false; }", service);
        Assert.Contains("if (!importUndoState.IsCurrent(undo)) throw", service);
        string changed = service[service.IndexOf("public void Changed(", StringComparison.Ordinal)..service.IndexOf("private FlowDeskData SnapshotData", StringComparison.Ordinal)];
        Assert.Equal(2, changed.Split("InvalidateImportUndo();").Length - 1); // ordinary edits and autosave's SaveNow path
        string flow = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", "SettingsTransferFlow.cs"));
        Assert.Contains("importUndo = null; transferBar.IsOpen = false;", flow);
        Assert.True(flow.IndexOf("services.ArmImportUndo(", StringComparison.Ordinal) > flow.IndexOf("await services.SaveNowAsync()", StringComparison.Ordinal));
        string undoMethod = service[service.IndexOf("internal async Task UndoImportAsync", StringComparison.Ordinal)..];
        Assert.True(undoMethod.IndexOf("InvalidateImportUndo();", StringComparison.Ordinal) > undoMethod.IndexOf("await Task.Run(undo.Backup.Restore)", StringComparison.Ordinal));
    }
    [Fact] public void ShutdownGuardsResumeBeforeTeardownAndContinueCleanupAfterIt()
    {
        string window = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", "MainWindow.xaml.cs"));
        Assert.Contains("if (!tearingDown) ResumeAfterFailedClose();", window);
        Assert.Contains("Cleanup(() => palette?.Dispose());", window);
        Assert.Contains("try { await services.Hotkeys.StopAllAsync(); } catch (Exception ex)", window);
        Assert.Contains("catch (Exception ex) { services.LogContextFailure(ex); }\n        finally { sampling = false; }", window.Replace("\r\n", "\n"));
    }
    [Fact] public void SettingsNullsNormalize()
    {
        var settings = AppSettings.FromJson("""{"Ai":null,"CustomCommands":{"Entries":null},"AutosaveOff":null,"AlwaysAddNewWindows":null,"FocusIconPaths":null,"DisabledShortcuts":null,"BrowserRestoreNoticesSeen":null}""");
        Assert.NotNull(settings.Ai); Assert.NotNull(settings.CustomCommands.Entries); Assert.NotNull(settings.AutosaveOff);
        Assert.NotNull(settings.AlwaysAddNewWindows); Assert.NotNull(settings.FocusIconPaths); Assert.NotNull(settings.DisabledShortcuts); Assert.NotNull(settings.BrowserRestoreNoticesSeen);
        Assert.NotNull(AppSettings.FromJson("{\"CustomCommands\":null}").CustomCommands);
    }
    [Fact] public void SettingsRecoveryUsesBackupAndPreservesBadOriginal()
    {
        string folder = Path.Combine(@"D:\Workspace\_tmp\opencode", "settings-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "app-settings.json");
            File.WriteAllText(path, "broken"); File.WriteAllText(path + ".bak", "{\"Theme\":\"Dark\"}");
            var recovery = new RecoverableJsonFile(path);
            var settings = recovery.Load(p => AppSettings.FromJson(File.ReadAllText(p)), () => new AppSettings(), out var aside);
            Assert.Equal("Dark", settings.Theme); Assert.Equal("broken", File.ReadAllText(aside!)); Assert.True(recovery.CanSave);
            File.WriteAllText(path + ".bak", "broken-backup");
            Assert.Equal("System", recovery.Load(p => AppSettings.FromJson(File.ReadAllText(p)), () => new AppSettings(), out _).Theme);
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact] public void ImportUndoExpiresOnFirstLaterChangeButNotImportOwnChanges()
    {
        var state = new ImportUndoState<object>(); var undo = new object(); int expired = 0;
        state.Invalidated += () => expired++;
        state.Invalidate(); // Import's own apply/save signals happen before completion arms Undo.
        state.Arm(undo); Assert.True(state.IsCurrent(undo)); Assert.Equal(0, expired);
        state.Invalidate(); Assert.False(state.IsCurrent(undo)); Assert.Equal(1, expired);
        state.Invalidate(); Assert.Equal(1, expired);
        state.Arm(new object()); Assert.False(state.IsCurrent(undo));
    }
    [Theory]
    [InlineData(0u, 0)] [InlineData(5u, 5)] [InlineData(1000u, 1000)] [InlineData(1001u, 1000)] [InlineData(uint.MaxValue, 1000)]
    public void PdfCountIsBoundedBeforeAllocation(uint input, int expected) => Assert.Equal(expected, PdfPreviewLimits.PageCount(input));
}
