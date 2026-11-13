// Pure planning/validation for Probe restore-watch. Linked into the core test project so the
// "every item must be the exact captured Chrome profile" rule is testable without a desktop.
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Probe;

/// <summary>Exact captured app identity: full exe identity plus browser ProcessKey (never a basename alone).</summary>
public readonly record struct WatchIdentity(string AppKey, string ProcessKey);

public sealed record ProfileIdentity(string Key, string Label);

public sealed class WatchPlan
{
    public required IReadOnlySet<WatchIdentity> Identities { get; init; }
    public required IReadOnlyList<ProfileIdentity> Profiles { get; init; }

    public bool IsWatched(WindowInfo w) =>
        string.IsNullOrEmpty(w.Aumid)
        && AppKeys.ExeName(w.ExePath) == "chrome"
        && Identities.Contains(new WatchIdentity(w.AppKey, BrowserSession.ProcessKey(w.ExePath, w.Arguments)));

    public bool IsCapturedChromeItem(WorkspaceItem item) =>
        item.App is { } app
        && string.IsNullOrEmpty(app.Aumid)
        && AppKeys.ExeName(app.ExePath) == "chrome"
        && Identities.Contains(IdentityFor(app));

    public static WatchIdentity IdentityFor(AppRef app) =>
        new(AppKeys.For(app.ExePath, null), BrowserSession.ProcessKey(app.ExePath, app.Arguments));
}

public static class RestoreWatchPlan
{
    /// <summary>
    /// restore-watch must not launch anything outside the captured Chrome profile. EVERY workspace
    /// item must therefore be a Chrome App/Website item with a non-empty captured --user-data-dir,
    /// no AUMID, and the exact captured exe identity.
    /// </summary>
    public static bool TryCreate(Workspace workspace, out WatchPlan plan, out string? error)
    {
        plan = null!;
        error = null;
        if (workspace.Items.Count == 0)
        {
            error = "the workspace is empty";
            return false;
        }

        var identities = new HashSet<WatchIdentity>();
        foreach (var item in workspace.Items)
        {
            if (item.Kind is not (ItemKind.App or ItemKind.Website))
            {
                error = $"item={item.Id} is {item.Kind}; restore-watch requires Chrome App/Website items only";
                return false;
            }
            if (item.App is not { } app || string.IsNullOrEmpty(app.ExePath))
            {
                error = $"item={item.Id} has no captured Chrome executable";
                return false;
            }
            if (!string.IsNullOrEmpty(app.Aumid))
            {
                error = $"item={item.Id} is a packaged app (AUMID); restore-watch requires a Chrome exe";
                return false;
            }
            if (AppKeys.ExeName(app.ExePath) != "chrome")
            {
                error = $"item={item.Id} uses {AppKeys.ExeName(app.ExePath)}; restore-watch only watches Chrome";
                return false;
            }
            string key = BrowserSession.ProcessKey(app.ExePath, app.Arguments);
            if (key.Length == 0)
            {
                error = $"item={item.Id} has no captured --user-data-dir; refusing the default profile";
                return false;
            }
            identities.Add(WatchPlan.IdentityFor(app));
        }

        var profiles = identities.Select(i => i.ProcessKey).Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => new ProfileIdentity(k, ProfileLabel(k))).ToList();
        plan = new WatchPlan { Identities = identities, Profiles = profiles };
        return true;
    }

    /// <summary>Short profile label: scheme + leaf + a stable hash. Full paths/command lines stay out of logs.</summary>
    internal static string ProfileLabel(string key)
    {
        if (key.Length == 0) return "(no profile)";
        int colon = key.IndexOf(':');
        string scheme = colon > 0 ? key[..colon] : "?";
        string tail = colon >= 0 && colon + 1 <= key.Length ? key[(colon + 1)..] : key;
        string leaf = tail.TrimEnd('\\', '/');
        int slash = leaf.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) leaf = leaf[(slash + 1)..];
        if (leaf.Length == 0) leaf = "?";
        return $"{scheme}:{leaf}#{ShortHash(key)}";
    }

    private static string ShortHash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s)
        {
            h ^= c;
            h *= 16777619;
        }
        return h.ToString("x8");
    }
}
