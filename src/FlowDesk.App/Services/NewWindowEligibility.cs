using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Services;

/// <summary>
/// A window may be offered as "new" only when it cannot be the intended replacement for ANY item that has no
/// live window yet. This is deliberately stricter than WindowMatcher.Assign, which returns one best assignment:
/// two windows that both score for one missing item must both stay out of the offer.
/// </summary>
public static class NewWindowEligibility
{
    public static bool MatchesMissingLiveItem(WindowInfo window, Workspace workspace,
        IReadOnlyDictionary<string, WindowInfo> liveBound)
    {
        foreach (var item in workspace.Items)
        {
            if (item.Kind == ItemKind.Website || liveBound.ContainsKey(item.Id)) continue;
            if (WindowMatcher.Score(window, MatchSpec.For(item)) >= WindowMatcher.Threshold) return true;
        }
        return false;
    }
}
