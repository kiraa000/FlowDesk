namespace FlowDesk.App.Services;

internal interface IClipboardBackup : IDisposable { void Restore(); }

internal static class ClipboardReplacement
{
    // Snapshot/allocation must finish before the destructive step. Keep the snapshot until all
    // formats, including privacy metadata, have transferred ownership successfully.
    internal static void Write(Func<IClipboardBackup> capture, Action empty, Action write)
    {
        using var backup = capture();
        empty();
        try { write(); }
        catch { backup.Restore(); throw; }
    }
}
