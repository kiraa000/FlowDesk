namespace FlowDesk.App.Autosave;

/// <summary>
/// Whether an autosave layout write is needed. Positions changed = write. No live window is only "nothing to do"
/// when no earlier Add or write is still pending; otherwise the retained item must be persisted on retry.
/// </summary>
public static class AutoSaveSavePolicy
{
    public static bool ShouldWrite(int updatedCount, bool pendingPersistence) =>
        updatedCount > 0 || pendingPersistence;
}
