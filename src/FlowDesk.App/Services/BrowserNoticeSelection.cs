using FlowDesk.Core.Model;

namespace FlowDesk.App.Services;

/// <summary>Which browser-restore notices a workspace should show, deduplicated by browser family.</summary>
public static class BrowserNoticeSelection
{
    public static IReadOnlyList<BrowserRestoreNotice> Unseen(Workspace workspace, Func<string, bool> hasSeen)
    {
        var notices = new List<BrowserRestoreNotice>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in workspace.Items.Where(i => i.ReopenLastTabs && BrowserRestoreHelp.IsBrowserItem(i)))
        {
            var notice = BrowserRestoreHelp.For(item.App!.ExePath, item.Name);
            if (!keys.Add(notice.Key) || hasSeen(notice.Key)) continue;
            notices.Add(notice);
        }
        return notices;
    }
}
