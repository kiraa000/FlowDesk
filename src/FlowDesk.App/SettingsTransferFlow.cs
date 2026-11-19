using FlowDesk.App.Services;
using FlowDesk.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace FlowDesk.App;

public sealed partial class MainWindow
{
    private bool transferOpen;
    private AppServices.ImportUndo? importUndo;
    private bool importReplaced;
    private InfoBar? transferBar;
    private bool TransferAvailable => loaded && !initializing && !closing && !endingSession && !exitRequested && App.ViewModels.CanTransfer;
    private void InitializeTransfer()
    {
        App.ViewModels.Settings.ExportTransfer = ExportSettingsAsync;
        App.ViewModels.Settings.ImportTransfer = ImportSettingsAsync;
        transferBar = new InfoBar { IsOpen = false, Message = "Imported. Your previous data is backed up.", Severity = InfoBarSeverity.Success };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var undo = new Button { Content = "Undo" }; var folder = new Button { Content = "Open folder" };
        undo.Click += async (_, _) => await UndoSettingsImportAsync();
        folder.Click += async (_, _) =>
        {
            try { if (importUndo is { } saved) await Windows.System.Launcher.LaunchFolderPathAsync(saved.Backup.Folder); }
            catch (Exception) { App.ViewModels.ShowStatus("The backup folder could not be opened.", StatusKind.Warning); }
        };
        actions.Children.Add(undo); actions.Children.Add(folder); transferBar.Content = actions;
        TransferStatusHost.Children.Add(transferBar);
        services.ImportUndoInvalidated += () => { importUndo = null; transferBar.IsOpen = false; };
    }
    private async Task ExportSettingsAsync()
    {
        if (transferOpen || !TransferAvailable) return;
        transferOpen = true;
        try
        {
            var selection = await new SettingsTransferDialogs(Root).ExportAsync();
            if (selection is null || !TransferAvailable) return;
            var picker = new FileSavePicker { SuggestedFileName = $"FlowDesk settings {DateTime.Now:yyyy-MM-dd}", DefaultFileExtension = ".flowdesk" };
            picker.FileTypeChoices.Add("FlowDesk settings", new List<string> { ".flowdesk" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null || !TransferAvailable) return;
            await App.ViewModels.ExecuteAsync(async () =>
            {
                var json = SettingsTransfer.Export(services.Data, services.Settings, selection, AppVersion.Display);
                await File.WriteAllTextAsync(file.Path, json, new System.Text.UTF8Encoding(false));
                App.ViewModels.ShowStatus("FlowDesk settings exported.", StatusKind.Success);
            });
        }
        catch (Exception) { App.ViewModels.ShowStatus("Settings could not be exported. Check the destination and try again.", StatusKind.Error); }
        finally { transferOpen = false; }
    }
    private async Task ImportSettingsAsync()
    {
        if (transferOpen || !TransferAvailable) return;
        transferOpen = true;
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".flowdesk"); picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null || !TransferAvailable) return;
            var preview = await SettingsTransfer.ReadAsync(file.Path);
            if (preview.Error is { } error) { App.ViewModels.ShowStatus(error, StatusKind.Error); return; }
            if (!TransferAvailable) return;
            var selection = await new SettingsTransferDialogs(Root).ImportAsync(preview, services.Data.Workspaces.Count, services.Settings.CustomCommands.Entries.Count);
            if (selection is null || !TransferAvailable) return;
            await App.ViewModels.ExecuteAsync(async () =>
            {
                // Re-evaluate current state after preview: autosave may have updated it while the dialog was open.
                var plan = await SettingsTransfer.PrepareAsync(services.Data, services.Settings, preview, selection);
                importUndo = await services.ImportAsync(plan);
                importReplaced = selection.Workspaces && selection.Replace && preview.Workspaces is not null;
                transferBar!.IsOpen = true;
                App.ViewModels.ReloadTransferViews(importReplaced);
                var warnings = plan.Warnings.ToList();
                try { warnings.AddRange(await App.ViewModels.Settings.ApplyTransferredSettingsAsync()); }
                catch (Exception) { warnings.Add("Some input settings could not be applied. Check Settings before using shortcuts."); }
                if (!await services.SaveNowAsync()) warnings.Add("Live settings changes could not be saved. Your backup is still available.");
                services.ArmImportUndo(importUndo!);
                if (warnings.Count > 0) App.ViewModels.ShowStatus(string.Join(Environment.NewLine, warnings), StatusKind.Warning);
                else App.ViewModels.StatusOpen = false;
            });
        }
        catch (Exception) { App.ViewModels.ShowStatus("Settings could not be imported. Your backup, if created, is in the data folder's backups folder.", StatusKind.Error); }
        finally { transferOpen = false; }
    }
    private async Task UndoSettingsImportAsync()
    {
        if (transferOpen || !TransferAvailable || importUndo is not { } saved) return;
        transferOpen = true;
        try
        {
            await App.ViewModels.ExecuteAsync(async () =>
            {
                await services.UndoImportAsync(saved);
                importUndo = null; transferBar!.IsOpen = false;
                App.ViewModels.ReloadTransferViews(importReplaced);
                var warnings = await App.ViewModels.Settings.ApplyTransferredSettingsAsync();
                await App.ViewModels.RestoreUndoActivationAsync();
                App.ViewModels.ShowStatus(warnings.Count == 0 ? "Import undone. Your previous data was restored." :
                    "Import undone. " + string.Join(Environment.NewLine, warnings), warnings.Count == 0 ? StatusKind.Success : StatusKind.Warning);
            });
        }
        finally { transferOpen = false; }
    }
}
