using System.ComponentModel;
using FlowDesk.App.Services;
using FlowDesk.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App;

public sealed partial class MainWindow
{
    private readonly HashSet<WorkspaceViewModel> trayWorkspaceSubscriptions = [];
    private bool trayWorkspacesClosed;

    private void InitializeTrayWorkspaces()
    {
        // H.NotifyIcon 2.4.1 PopupMenu repopulates from the live flyout on EVERY opening.
        // Its shipped WinUI assembly disables preview events (and ignores flyout.Visibility),
        // so keep Items current instead of relying on Flyout.Opening/Preview events.
        App.ViewModels.WorkspacesCollection.CollectionChanged += TrayWorkspaceCollectionChanged;
        services.StateChanged += RefreshTrayWorkspaces;
        Closed += (_, _) =>
        {
            trayWorkspacesClosed = true;
            services.StateChanged -= RefreshTrayWorkspaces;
            App.ViewModels.WorkspacesCollection.CollectionChanged -= TrayWorkspaceCollectionChanged;
            foreach (var vm in trayWorkspaceSubscriptions) vm.PropertyChanged -= TrayWorkspaceChanged;
            trayWorkspaceSubscriptions.Clear();
        };
        RefreshTrayWorkspaces();
    }

    private void TrayWorkspaceCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        RefreshTrayWorkspaces();

    private void TrayWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.Name) or nameof(WorkspaceViewModel.IsActive) or null or "")
            RefreshTrayWorkspaces();
    }

    private void RefreshTrayWorkspaces()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(RefreshTrayWorkspaces);
            return;
        }
        if (trayWorkspacesClosed) return;
        var workspaces = App.ViewModels.WorkspacesCollection;
        foreach (var removed in trayWorkspaceSubscriptions.Where(vm => !workspaces.Contains(vm)).ToArray())
        {
            removed.PropertyChanged -= TrayWorkspaceChanged;
            trayWorkspaceSubscriptions.Remove(removed);
        }
        foreach (var vm in workspaces)
            if (trayWorkspaceSubscriptions.Add(vm)) vm.PropertyChanged += TrayWorkspaceChanged;

        TrayWorkspaces.Items.Clear();
        foreach (var workspace in workspaces)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = UxDecisions.TrayWorkspaceName(workspace.Name),
                IsChecked = workspace.IsActive,
                IsEnabled = !initializing && !closing && !endingSession && !exitRequested && !App.ViewModels.IsBusy,
                // A Win32 popup is a snapshot while open: recheck lifetime/membership at click time,
                // then invoke the exact card command (busy/progress/error handling remain shared).
                Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                {
                    if (!initializing && !closing && !endingSession && !exitRequested && !allowClose &&
                        !App.ViewModels.IsBusy && App.ViewModels.WorkspacesCollection.Contains(workspace))
                        workspace.OpenCommand.Execute(null);
                })
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, workspace.Name);
            // Native PopupMenu does not copy tooltips, but retain it for renderers that do.
            ToolTipService.SetToolTip(item, workspace.Name);
            TrayWorkspaces.Items.Add(item);
        }
        if (TrayWorkspaces.Items.Count == 0)
            TrayWorkspaces.Items.Add(new MenuFlyoutItem { Text = "No workspaces yet", IsEnabled = false });
    }
}
