using System.Text.Json;
using FlowDesk.Core.Restore;

namespace FlowDesk.Core.Palette;

public enum BookmarkBrowser { Edge, Chrome, Firefox }
public sealed record BookmarkOptions(bool Edge = true, bool Chrome = true, bool Firefox = true)
{
    public bool Enabled(BookmarkBrowser browser) => browser switch { BookmarkBrowser.Edge => Edge, BookmarkBrowser.Chrome => Chrome, _ => Firefox };
}
public sealed record BookmarkProfile(BookmarkBrowser Browser, string Name, string Path, string Executable)
{
    public string Arguments => BrowserSession.ProfileArgs(Executable, Browser == BookmarkBrowser.Firefox
        ? "-profile " + CustomCommandRules.Quote(Path)
        : "--user-data-dir=" + CustomCommandRules.Quote(System.IO.Path.GetDirectoryName(Path)!) + " --profile-directory=" + CustomCommandRules.Quote(System.IO.Path.GetFileName(Path))) ?? "";
}
public sealed record BookmarkEntry(string Key, string Title, string Url, BookmarkProfile Profile)
{
    public string Id => "bookmark:" + Profile.Browser + ":" + Profile.Path.ToUpperInvariant() + ":" + Key;
    public string DedupeKey => "bookmark:" + Profile.Browser + ":" + Profile.Path.TrimEnd('\\', '/').Replace('/', '\\').ToUpperInvariant() + ":" + new Uri(Url).AbsoluteUri;
    public string Subtitle => new Uri(Url).Host + " · " + Profile.Browser + " · " + Profile.Name;
}
public static class BookmarkParser
{
    public static BookmarkEntry? Entry(string key, string title, string url, BookmarkProfile profile)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return new(key.Length > 0 ? key : uri.AbsoluteUri, string.IsNullOrWhiteSpace(title) ? uri.Host : title, uri.AbsoluteUri, profile);
    }
    public static IReadOnlyList<BookmarkEntry> Chromium(string json, BookmarkProfile profile)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var result = new List<BookmarkEntry>();
        if (document.RootElement.TryGetProperty("roots", out var roots))
            foreach (var root in roots.EnumerateObject()) Walk(root.Value);
        return result.DistinctBy(b => b.DedupeKey).ToArray();
        void Walk(JsonElement node)
        {
            if (result.Count >= 10000 || node.ValueKind != JsonValueKind.Object) return;
            if (String(node, "type") == "url" && Entry(String(node, "id"), String(node, "name"), String(node, "url"), profile) is { } entry) result.Add(entry);
            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                foreach (var child in children.EnumerateArray()) Walk(child);
        }
    }
    private static string String(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    public static IReadOnlyList<BookmarkProfile> FirefoxProfiles(string ini, string root, string executable)
    {
        var result = new List<BookmarkProfile>(); Dictionary<string, string>? section = null;
        foreach (string line in ini.Split('\n').Append("[end]"))
        {
            string text = line.Trim();
            if (text.StartsWith('['))
            {
                if (section is not null && section.TryGetValue("Path", out var path))
                {
                    bool relative = section.GetValueOrDefault("IsRelative") != "0";
                    string resolved = relative ? System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path)) : path;
                    if (System.IO.Path.IsPathFullyQualified(resolved)) result.Add(new(BookmarkBrowser.Firefox, section.GetValueOrDefault("Name") ?? System.IO.Path.GetFileName(resolved), resolved, executable));
                }
                section = text.StartsWith("[Profile", StringComparison.OrdinalIgnoreCase) ? new(StringComparer.OrdinalIgnoreCase) : null;
            }
            else if (section is not null && text.Length > 0 && text[0] is not (';' or '#') && text.IndexOf('=') is int equals && equals > 0)
                section[text[..equals].Trim()] = text[(equals + 1)..].Trim();
        }
        return result.DistinctBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
