using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.App.Autosave;
using FlowDesk.App.Services;

namespace FlowDesk.App.ViewModels;

/// <summary>Non-modal new-window offer and the process-wide autosave loop state.</summary>
public partial class AutoSaveViewModel : ObservableObject
{
    private readonly AppViewModels _owner;
    private readonly AutoSaveCoordinator _coordinator;
    [ObservableProperty] public partial bool OfferOpen { get; set; }
    [ObservableProperty] public partial string OfferText { get; set; } = "";
    private string _offerWorkspaceId = "";
    private long _offerHandle;
    private long _offerGeneration;

    internal AutoSaveViewModel(AppViewModels owner)
    {
        _owner = owner;
        var host = new AppAutoSaveHost(owner.Services, owner.Ui,
            id => owner.FindWorkspace(id),
            id => owner.ActiveWorkspaceId == id,
            () => owner.IsBusy,
            status => owner.ApplyAutoSaveStatus(status),
            offer => ShowOffer(offer),
            HideOffer,
            owner.ShowAutoSaveProblem,
            owner.RefreshActivePositions,
            owner.RefreshActiveItems);
        _coordinator = new AutoSaveCoordinator(host, new SystemAutoSaveClock(), () => owner.IsBusy);
    }

    internal AutoSaveCoordinator Coordinator => _coordinator;
    internal void Start() => _coordinator.Start();
    internal Task StopAsync() => _coordinator.StopAsync();
    internal Task SuspendAsync() => _coordinator.SuspendAsync();

    private void ShowOffer(NewWindowOffer offer)
    {
        if (offer.WorkspaceId != _owner.ActiveWorkspaceId || _owner.IsBusy) return;
        if (!_coordinator.IsCurrent(offer.WorkspaceId, offer.Generation)) return;
        if (offer.Generation < _offerGeneration) return; // queued older offer
        _offerWorkspaceId = offer.WorkspaceId;
        _offerHandle = offer.Handle;
        _offerGeneration = offer.Generation;
        OfferText = $"Add {offer.Name} to {_owner.WorkspaceName(offer.WorkspaceId)}?";
        OfferOpen = true;
    }

    private void HideOffer(long generation)
    {
        // A stale dismissal must not close a newer generation's offer.
        if (generation != 0 && _offerGeneration != 0 && generation < _offerGeneration) return;
        if (generation != 0 && _offerGeneration == 0) return;
        HideOfferLocal();
    }

    private void HideOfferLocal()
    {
        OfferOpen = false;
        _offerWorkspaceId = "";
        _offerHandle = 0;
        _offerGeneration = 0;
    }

    [RelayCommand]
    private async Task AcceptOfferAsync()
    {
        if (!OfferOpen) return;
        string workspaceId = _offerWorkspaceId;
        long handle = _offerHandle;
        long generation = _offerGeneration;
        HideOfferLocal();
        var outcome = await RunAsync(() => _coordinator.AcceptAsync(handle, generation));
        if (_owner.ActiveWorkspaceId != workspaceId) return;
        if (outcome == AddWindowOutcome.NotEligible)
            _owner.ShowStatus("That window is no longer available.", StatusKind.Information);
        else if (outcome == AddWindowOutcome.PersistenceFailed)
            _owner.ShowStatus("The window was added, but its workspace item could not be saved. Autosave will retry.",
                StatusKind.Warning);
    }

    [RelayCommand]
    private void DeclineOffer()
    {
        _coordinator.Decline(_offerHandle, _offerGeneration);
        HideOfferLocal();
    }

    [RelayCommand]
    private async Task AlwaysAddOfferAsync()
    {
        if (!OfferOpen) return;
        string workspaceId = _offerWorkspaceId;
        long handle = _offerHandle;
        long generation = _offerGeneration;
        _owner.PersistAlwaysAdd(workspaceId, true);
        _coordinator.SetAlwaysAdd(true);
        HideOfferLocal();
        await RunAsync(() => _coordinator.AcceptAsync(handle, generation));
    }

    private async Task<AddWindowOutcome> RunAsync(Func<Task<AddWindowOutcome>> action)
    {
        try { return await Task.Run(action); }
        catch (Exception ex)
        {
            _owner.ShowStatus("The window could not be added. " + ex.Message, StatusKind.Error);
            return AddWindowOutcome.NotEligible;
        }
    }
}
