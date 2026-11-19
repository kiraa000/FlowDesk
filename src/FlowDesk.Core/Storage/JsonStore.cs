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
    private RecoverableJsonFile? recovery;

    public string Folder { get; } = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowDesk");
    public string FilePath => Path.Combine(Folder, "flowdesk.json");
    public bool CanSave => recovery?.CanSave ?? true;

    /// <summary>Loads the data; a missing file gives defaults. <paramref name="recoveredFrom"/> is set when the main file was unreadable.</summary>
    public FlowDeskData Load(out string? recoveredFrom)
    {
        recovery ??= new(FilePath);
        return recovery.Load(Read, () => new FlowDeskData(), out recoveredFrom);
    }

    private static FlowDeskData Read(string path)
    {
        using var fs = File.OpenRead(path);
        var data = JsonSerializer.Deserialize<FlowDeskData>(fs, Options) ?? throw new InvalidDataException("Empty file");
        if (data.Version > FlowDeskData.CurrentVersion)
            throw new InvalidDataException($"File is from a newer FlowDesk (version {data.Version})");
        data.Workspaces ??= [];
        data.Workspaces.RemoveAll(w => w is null);
        data.Focus ??= new();
        data.Focus.Blocked ??= []; data.Focus.Allowed ??= [];
        data.Focus.Blocked.RemoveAll(r => r is null); data.Focus.Allowed.RemoveAll(r => r is null);
        foreach (var workspace in data.Workspaces)
        {
            workspace.Items ??= [];
            workspace.Items.RemoveAll(i => i is null);
            foreach (var item in workspace.Items) item.Urls ??= [];
        }
        return data;
    }

    public async Task SaveAsync(FlowDeskData data, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            recovery?.EnsureCanSave();
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
