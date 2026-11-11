using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.App.Services;

namespace FlowDesk.App.ViewModels;

/// <summary>
/// One non-modal, app-wide browser session-restore notice. Its keys are marked seen only when the InfoBar is
/// actually visible and the user presses Got it; switching away hides it without acknowledging anything.
/// </summary>
public partial class BrowserNoticeViewModel : ObservableObject
{
    private readonly AppViewModels _owner;
    private List<string> _visibleKeys = [];

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string NoticeText { get; set; } = "";

    internal BrowserNoticeViewModel(AppViewModels owner) => _owner = owner;

    internal void ShowFor(WorkspaceViewModel? workspace)
    {
        IReadOnlyList<BrowserRestoreNotice> notices = workspace is null
            ? []
            : BrowserNoticeSelection.Unseen(workspace.Model, _owner.Services.Settings.HasSeenBrowserNotice);
        if (notices.Count == 0)
        {
            Hide();
            return;
        }
        _visibleKeys = notices.Select(n => n.Key).ToList();
        NoticeText = "Each browser must restore its own tabs. "
            + string.Join(" ", notices.Select(n => n.Instructions)) + " " + BrowserRestoreHelp.Caveat;
        IsOpen = true;
    }

    internal void Hide()
    {
        IsOpen = false;
        NoticeText = "";
        _visibleKeys = [];
    }

    [RelayCommand]
    private void Dismiss()
    {
        // Only keys that this visible notice actually listed can be acknowledged.
        if (!IsOpen) return;
        foreach (string key in _visibleKeys) _owner.Services.Settings.MarkBrowserNoticeSeen(key);
        if (_visibleKeys.Count > 0) _owner.Services.Changed();
        Hide();
    }
}
