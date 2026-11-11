using System.Text.Json;
using System.Text.Json.Serialization;
using FlowDesk.Core.Model;

namespace FlowDesk.Core.Storage;

/// <summary>Everything FlowDesk persists, in one versioned file.</summary>
public sealed class FlowDeskData
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public List<Workspace> Workspaces { get; set; } = [];
    public FocusSettings Focus { get; set; } = new();
    /// <summary>Set once the user finished or skipped the onboarding tour.</summary>
    public bool TourCompleted { get; set; }
}

/// <summary>
/// JSON persistence in %LOCALAPPDATA%\FlowDesk\flowdesk.json. Writes are atomic (temp file + replace) and
/// keep one backup; a corrupt file is set aside instead of being overwritten, so data is never silently lost.
/// </summary>
public sealed class JsonStore(string? folder = null)
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    public string Folder { get; } = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowDesk");
    public string FilePath => Path.Combine(Folder, "flowdesk.json");

    /// <summary>Loads the data; a missing file gives defaults. <paramref name="recoveredFrom"/> is set when the main file was unreadable.</summary>
    public FlowDeskData Load(out string? recoveredFrom)
    {
        recoveredFrom = null;
        if (!File.Exists(FilePath)) return new FlowDeskData();
        try
        {
            return Read(FilePath);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            string aside = FilePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(FilePath, aside, overwrite: true);
            recoveredFrom = aside;
            string bak = FilePath + ".bak";
            try { return File.Exists(bak) ? Read(bak) : new FlowDeskData(); }
            catch (Exception e2) when (e2 is JsonException or IOException or InvalidDataException) { return new FlowDeskData(); }
        }
    }

    private static FlowDeskData Read(string path)
    {
        using var fs = File.OpenRead(path);
        var data = JsonSerializer.Deserialize<FlowDeskData>(fs, Options) ?? throw new InvalidDataException("Empty file");
        if (data.Version > FlowDeskData.CurrentVersion)
            throw new InvalidDataException($"File is from a newer FlowDesk (version {data.Version})");
        return data;
    }

    public async Task SaveAsync(FlowDeskData data, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                await JsonSerializer.SerializeAsync(fs, data, Options, ct).ConfigureAwait(false);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak", ignoreMetadataErrors: true);
            else File.Move(tmp, FilePath);
        }
        finally { _lock.Release(); }
    }
}
