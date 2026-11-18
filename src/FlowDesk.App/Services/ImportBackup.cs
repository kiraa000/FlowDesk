using System.Text.RegularExpressions;

namespace FlowDesk.App.Services;

/// <summary>Only fixed data filenames are backed up. Backup handles originate here, never in imported JSON.</summary>
public sealed class ImportBackup
{
    private static readonly string[] Names = ["flowdesk.json", "app-settings.json"];
    public string Folder { get; }
    private readonly string dataFolder;
    private readonly Dictionary<string, byte[]?> originals;
    private ImportBackup(string dataFolder, string folder, Dictionary<string, byte[]?> originals)
    { this.dataFolder = dataFolder; Folder = folder; this.originals = originals; }
    public static ImportBackup Create(string dataFolder, DateTime? now = null)
    {
        Directory.CreateDirectory(dataFolder);
        string root = Path.Combine(dataFolder, "backups");
        Directory.CreateDirectory(root); RejectLink(root);
        string stem = "before-import-" + (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss");
        string folder = Path.Combine(root, stem); int suffix = 1;
        while (Directory.Exists(folder)) folder = Path.Combine(root, stem + "-" + (suffix++).ToString("D3"));
        Directory.CreateDirectory(folder);
        var originals = new Dictionary<string, byte[]?>();
        foreach (string name in Names)
        {
            string source = Path.Combine(dataFolder, name);
            byte[]? bytes = File.Exists(source) ? File.ReadAllBytes(source) : null;
            originals[name] = bytes;
            if (bytes is not null) File.WriteAllBytes(Path.Combine(folder, name), bytes);
        }
        // A complete backup is required before any caller writes. Pruning is best effort and never follows links.
        var backup = new ImportBackup(dataFolder, folder, originals);
        try
        {
            var candidates = Directory.GetDirectories(root).Where(p => Regex.IsMatch(Path.GetFileName(p),
                @"^before-import-\d{8}-\d{6}(?:-\d{3,})?$", RegexOptions.CultureInvariant))
                .OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal).ToList();
            // Always retain this import even if the system clock moved backwards.
            candidates.Remove(folder); candidates.Insert(0, folder);
            foreach (var old in candidates.Skip(5))
            {
                RejectLink(old);
                if (Directory.GetDirectories(old).Length != 0) continue;
                var files = Directory.GetFiles(old);
                if (files.Any(f => !Names.Contains(Path.GetFileName(f)))) continue;
                foreach (var file in files) RejectLink(file);
                foreach (var file in files) File.Delete(file);
                Directory.Delete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Retention must not defeat a completed backup. */ }
        return backup;
    }
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked backup locations are not supported.");
    }
    public void Restore()
    {
        // Use the captured bytes, not mutable backup files, for this-run Undo.
        foreach (var pair in originals)
        {
            string path = Path.Combine(dataFolder, pair.Key);
            if (pair.Value is null) { if (File.Exists(path)) File.Delete(path); continue; }
            AtomicWrite(path, pair.Value);
        }
    }
    public static void AtomicWrite(string path, byte[] bytes)
    {
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", true);
        else File.Move(tmp, path);
    }
}
