using FlowDesk.Core.Model;

namespace FlowDesk.App.Services;

/// <summary>Edits content only; identity, browser preferences and the saved placement are retained.</summary>
internal static class WorkspaceItemEdits
{
    public static string? Apply(WorkspaceItem source, string name, string target, string arguments,
        Func<string, bool> fileExists, Func<string, bool> folderExists, out WorkspaceItem edited)
    {
        edited = source.Clone();
        if (string.IsNullOrWhiteSpace(name)) return "Enter a name.";
        edited.Name = name.Trim();
        switch (source.Kind)
        {
            case ItemKind.Website:
                var urls = target.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
                if (urls.Count == 0 || urls.Any(s => !Uri.TryCreate(s, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host)))
                    return "Enter complete http or https links, one per line.";
                edited.Urls = urls; break;
            case ItemKind.File:
            case ItemKind.Folder:
                string path = target.Trim();
                if (path.Length == 0 || !(source.Kind == ItemKind.Folder ? folderExists(path) : fileExists(path)))
                    return source.Kind == ItemKind.Folder ? "Choose an existing folder." : "Choose an existing file.";
                edited.Path = path; break;
            case ItemKind.App:
                if (edited.App is null) return "This app has no launch details to edit.";
                edited.App.Arguments = arguments; break;
            default: return "This item cannot be edited.";
        }
        return null;
    }
}
