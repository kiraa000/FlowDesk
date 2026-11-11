using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Restore;

/// <summary>What window an item expects to own. Built from the item (and the launch, for websites/files).</summary>
public sealed record MatchSpec
{
    public required ItemKind Kind { get; init; }
    /// <summary>Exact app identity (exe path or AUMID key); null = unknown.</summary>
    public string? AppKey { get; init; }
    /// <summary>Exe file name ("chrome"), used when the path differs (updates, launchers).</summary>
    public string? ExeName { get; init; }
    public string? Aumid { get; init; }
    /// <summary>Saved window title, if any.</summary>
    public string? Title { get; init; }
    /// <summary>Text the title must contain (file name, folder name). Required for File/Folder items.</summary>
    public string? RequiredTitlePart { get; init; }
    public string? ClassName { get; init; }

    public static MatchSpec For(WorkspaceItem item, string? browserExe = null, string? fileAppExe = null) => item.Kind switch
    {
        ItemKind.App => new MatchSpec
        {
            Kind = item.Kind, AppKey = item.App?.Key, ExeName = AppKeys.ExeName(item.App?.ExePath),
            Aumid = item.App?.Aumid, Title = item.Window?.Title, ClassName = item.Window?.ClassName,
        },
        ItemKind.Website => new MatchSpec
        {
            Kind = item.Kind, ExeName = AppKeys.ExeName(browserExe ?? item.App?.ExePath), Title = item.Window?.Title,
        },
        ItemKind.File => new MatchSpec
        {
            Kind = item.Kind, ExeName = AppKeys.ExeName(fileAppExe ?? item.App?.ExePath), Title = item.Window?.Title,
            RequiredTitlePart = System.IO.Path.GetFileNameWithoutExtension(item.Path ?? ""),
        },
        _ => new MatchSpec
        {
            Kind = item.Kind, ExeName = "explorer", ClassName = "CabinetWClass", Title = item.Window?.Title,
            RequiredTitlePart = FolderTitle(item.Path),
        },
    };

    private static string FolderTitle(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        string trimmed = path.TrimEnd('\\', '/');
        string name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name; // "C:\" -> "C:"
    }
}

/// <summary>Scores how well an open window fits an item. Pure; 0 means "not this item's window".</summary>
public static class WindowMatcher
{
    public const int Threshold = 60;

    public static int Score(WindowInfo w, MatchSpec s)
    {
        if (w.Unresolved) return 0;

        int score;
        if (!string.IsNullOrEmpty(s.Aumid))
            score = string.Equals(w.Aumid, s.Aumid, StringComparison.OrdinalIgnoreCase) ? 100 : 0;
        else if (s.AppKey is not null && s.AppKey == w.AppKey) score = 100;
        else if (!string.IsNullOrEmpty(s.ExeName) && s.ExeName == w.ExeName) score = 80;
        else score = 0;
        if (score == 0) return 0;

        if (s.ClassName is { Length: > 0 } cls)
        {
            if (s.Kind == ItemKind.Folder && w.ClassName != cls) return 0; // explorer.exe also owns non-folder windows
            if (w.ClassName == cls) score += 5;
        }

        if (!string.IsNullOrEmpty(s.RequiredTitlePart))
        {
            if (w.Title.IndexOf(s.RequiredTitlePart, StringComparison.OrdinalIgnoreCase) < 0) return 0;
            score += 15;
        }

        score += TitleSimilarity(w.Title, s.Title);
        return score;
    }

    /// <summary>0..30: exact title 30, containment 20, otherwise word overlap.</summary>
    public static int TitleSimilarity(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 30;
        if (a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase)) return 20;
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;
        int inter = ta.Count(tb.Contains);
        int union = ta.Count + tb.Count - inter;
        return (int)Math.Round(20.0 * inter / union);
    }

    private static HashSet<string> Tokens(string s) =>
        s.Split([' ', '-', '—', '–', '|', '·', '•', ':', '\\', '/', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
         .Select(t => t.ToLowerInvariant()).Where(t => t.Length > 1).ToHashSet();

    /// <summary>
    /// Assigns windows to items greedily by best score so two items never share a window.
    /// <paramref name="bonus"/> lets the caller prefer e.g. windows that appeared after a launch.
    /// </summary>
    public static Dictionary<string, WindowInfo> Assign(
        IReadOnlyList<(string ItemId, MatchSpec Spec)> items,
        IReadOnlyList<WindowInfo> windows,
        ISet<long> claimed,
        Func<string, WindowInfo, int>? bonus = null,
        Func<string, WindowInfo, bool>? allowed = null)
    {
        var pairs = new List<(int Score, int ItemIdx, WindowInfo W)>();
        for (int i = 0; i < items.Count; i++)
            foreach (var w in windows)
            {
                if (claimed.Contains(w.Handle)) continue;
                if (allowed is not null && !allowed(items[i].ItemId, w)) continue;
                int sc = Score(w, items[i].Spec);
                if (sc == 0) continue;
                sc += bonus?.Invoke(items[i].ItemId, w) ?? 0;
                if (sc >= Threshold) pairs.Add((sc, i, w));
            }

        var result = new Dictionary<string, WindowInfo>();
        var used = new HashSet<long>();
        // Highest score first; ties go to the earlier item, then the frontmost window (list order).
        foreach (var (_, idx, w) in pairs.OrderByDescending(p => p.Score).ThenBy(p => p.ItemIdx))
        {
            string id = items[idx].ItemId;
            if (result.ContainsKey(id) || used.Contains(w.Handle)) continue;
            result[id] = w;
            used.Add(w.Handle);
        }
        return result;
    }
}
