using System.Text.Json;

namespace FlowDesk.Core.Storage;

/// <summary>Preserves unreadable originals before permitting another write to their path.</summary>
public sealed class RecoverableJsonFile(string path)
{
    public bool CanSave { get; private set; } = true;
    public T Load<T>(Func<string, T> read, Func<T> defaults, out string? recoveredFrom)
    {
        recoveredFrom = null;
        try { return read(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return defaults(); }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            // Do not overwrite a previous recovery copy, even on repeated starts in the same second.
            string aside = path + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            try { File.Copy(path, aside, overwrite: false); recoveredFrom = aside; }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            { CanSave = false; recoveredFrom = path; }
            try { return read(path + ".bak"); }
            catch (Exception backupError) when (IsLoadFailure(backupError)) { return defaults(); }
        }
    }

    public void EnsureCanSave()
    {
        if (!CanSave) throw new IOException("The unreadable original could not be backed up. Saving is disabled to preserve it.");
    }
    private static bool IsLoadFailure(Exception ex) => ex is JsonException or IOException or InvalidDataException or UnauthorizedAccessException;
}
