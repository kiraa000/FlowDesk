using System.Text;
using FlowDesk.Core.Model;

namespace FlowDesk.Core.Restore;

/// <summary>
/// "Reopen my last tabs" for browser App items: FlowDesk starts the browser with only its PROFILE arguments and
/// no links, so the browser's own "continue where you left off" restores the tabs. FlowDesk never reads tabs.
///
/// Profile identity comes in two levels:
/// - <see cref="ProcessKey"/>: what separates browser PROCESSES. Chromium runs every profile of one user-data-dir
///   in one process, so only --user-data-dir separates processes; Gecko runs one process per profile (-P / -profile).
///   This is all that can be read from an open window (its process command line), so reuse matching uses it.
/// - <see cref="LaunchKey"/>: what one launch restores. Adds Chromium's --profile-directory, because launching
///   profile "Work" does not restore profile "Default".
/// </summary>
public static class BrowserSession
{
    private static readonly string[] Gecko = ["firefox", "librewolf", "waterfox", "floorp", "zen"];

    public static bool IsGecko(string? exePath) => Gecko.Contains(AppKeys.ExeName(exePath));

    /// <summary>True when this item is restored through the browser's own session restore.</summary>
    public static bool Applies(WorkspaceItem item) =>
        item.Kind == ItemKind.App && item.ReopenLastTabs && item.App is { } app
        && string.IsNullOrEmpty(app.Aumid) && ShellLauncher.IsBrowser(app.ExePath);

    /// <summary>
    /// Keeps only the arguments that pick the profile; drops links, window flags and everything else.
    /// Chromium: --user-data-dir, --profile-directory. Gecko: -P/-p name, -profile/--profile path, -no-remote.
    /// Returns null when nothing is left.
    /// </summary>
    public static string? ProfileArgs(string? exePath, string? arguments)
    {
        var tokens = Tokenize(arguments);
        var kept = new List<string>();
        if (IsGecko(exePath))
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                string t = tokens[i];
                string flag = t.TrimStart('-').ToLowerInvariant();
                bool dashed = t.StartsWith('-');
                if (dashed && (flag == "p" || flag == "profile") && i + 1 < tokens.Count && !tokens[i + 1].StartsWith('-'))
                {
                    kept.Add((flag == "p" ? "-P " : "-profile ") + Quote(tokens[i + 1]));
                    i++;
                }
                else if (dashed && flag == "no-remote") kept.Add("-no-remote");
            }
        }
        else
        {
            foreach (string t in tokens)
                if (ChromiumValue(t, "--user-data-dir") is { } udd) kept.Add("--user-data-dir=" + Quote(udd));
                else if (ChromiumValue(t, "--profile-directory") is { } pd) kept.Add("--profile-directory=" + Quote(pd));
        }
        return kept.Count == 0 ? null : string.Join(' ', kept);
    }

    /// <summary>Identity of the browser process an item/window belongs to ("" = the default profile/data dir).</summary>
    public static string ProcessKey(string? exePath, string? arguments)
    {
        var tokens = Tokenize(arguments);
        if (IsGecko(exePath))
        {
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                string flag = tokens[i].TrimStart('-').ToLowerInvariant();
                if (!tokens[i].StartsWith('-') || tokens[i + 1].StartsWith('-')) continue;
                if (flag == "p") return "p:" + tokens[i + 1].Trim().ToLowerInvariant();
                if (flag == "profile") return "profile:" + NormalizePath(tokens[i + 1]);
            }
            return "";
        }
        foreach (string t in tokens)
            if (ChromiumValue(t, "--user-data-dir") is { } udd) return "udd:" + NormalizePath(udd);
        return "";
    }

    /// <summary>What one launch restores: the process key plus Chromium's profile directory.</summary>
    public static string LaunchKey(AppRef app)
    {
        string key = app.Key + "|" + ProcessKey(app.ExePath, app.Arguments);
        if (IsGecko(app.ExePath)) return key;
        string? pd = Tokenize(app.Arguments).Select(t => ChromiumValue(t, "--profile-directory")).FirstOrDefault(v => v is not null);
        return key + "|" + (pd ?? "default").Trim().ToLowerInvariant();
    }

    /// <summary>"--key=value" or "--key=\"value\"" (already unquoted by the tokenizer) -> value; else null.</summary>
    private static string? ChromiumValue(string token, string key)
    {
        if (!token.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return null;
        string v = token[(key.Length + 1)..].Trim();
        return v.Length == 0 ? null : v;
    }

    private static string NormalizePath(string p) => p.Trim().TrimEnd('\\', '/').Replace('/', '\\').ToLowerInvariant();

    private static string Quote(string v) => v.Contains(' ') || v.Contains('\t') ? "\"" + v + "\"" : v;

    /// <summary>
    /// Splits a Windows command line like CommandLineToArgvW for the cases browsers produce: double quotes group
    /// (also mid-token: --user-data-dir="C:\a b"), quotes are removed. Backslash escapes are not interpreted,
    /// so a path ending in a backslash before a quote stays intact.
    /// </summary>
    public static List<string> Tokenize(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return result;
        var cur = new StringBuilder();
        bool inQuotes = false, any = false;
        foreach (char c in commandLine)
        {
            if (c == '"') { inQuotes = !inQuotes; any = true; continue; }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (any) { result.Add(cur.ToString()); cur.Clear(); any = false; }
                continue;
            }
            cur.Append(c);
            any = true;
        }
        if (any) result.Add(cur.ToString());
        return result;
    }
}
