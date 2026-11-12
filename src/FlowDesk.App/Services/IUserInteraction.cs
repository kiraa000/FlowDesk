using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;

namespace FlowDesk.App.Services;

public interface IUserInteraction
{
    void Enqueue(Action action);
    Task<bool> ConfirmAsync(string title, string message, string primary);
    Task<string?> PickPathAsync(ItemKind kind);
    Task<string?> PickFileAsync(string extension);
    Task<WorkspaceItem?> NewWebsiteAsync();
    Task<CaptureCandidate?> PickRunningAsync(IReadOnlyList<CaptureCandidate> candidates);
    Task ShowReportAsync(RestoreReport report);
}
