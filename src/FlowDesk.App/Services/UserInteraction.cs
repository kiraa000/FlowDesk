using FlowDesk.App.ViewModels;
using FlowDesk.App.Views;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace FlowDesk.App.Services;

// Presentation-only interactions. All workspace decisions remain in the view models.
public sealed class UserInteraction(Window window, FrameworkElement root) : IUserInteraction
{
    // Fire-and-forget status updates may be dropped at shutdown; awaited work must use TryEnqueue.
    public void Enqueue(Action action) => TryEnqueue(action);
    public bool TryEnqueue(Action action) => window.DispatcherQueue.TryEnqueue(() => action());
    private ContentDialog Dialog(string title, UIElement content, string primary = "", string close = "Cancel") => new()
    {
        XamlRoot = root.XamlRoot, RequestedTheme = root.RequestedTheme,
        Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close,
        DefaultButton = primary.Length == 0 ? ContentDialogButton.Close : ContentDialogButton.Primary
    };
    public async Task<bool> ConfirmAsync(string title, string message, string primary) =>
        await Dialog(title, Ui.Text(message), primary).ShowAsync() == ContentDialogResult.Primary;
    public async Task<string?> PickPathAsync(ItemKind kind)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (kind == ItemKind.Folder)
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            return (await picker.PickSingleFolderAsync())?.Path;
        }
        else
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(kind == ItemKind.App ? ".exe" : "*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            return (await picker.PickSingleFileAsync())?.Path;
        }
    }
    public async Task<string?> PickFileAsync(string extension)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return (await picker.PickSingleFileAsync())?.Path;
    }
    public async Task<WorkspaceItem?> NewWebsiteAsync()
    {
        var content = Ui.Stack();
        var name = new TextBox { Header = "Name", Text = "Websites", MaxLength = 120 };
        var links = new TextBox
        {
            Header = "Links, one per line", AcceptsReturn = true, MinHeight = 120, TextWrapping = TextWrapping.Wrap
        };
        var error = Ui.Text("Uses your default browser. Only http and https links are supported.", secondary: true);
        content.Children.Add(name); content.Children.Add(links); content.Children.Add(error);
        var dialog = Dialog("Add website", content, "Add website");
        List<string> urls = [];
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text) && LinkValidation.TryParse(links.Text, out urls)) return;
            args.Cancel = true; error.Text = "Enter a name and complete http or https links.";
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return new WorkspaceItem { Kind = ItemKind.Website, Name = name.Text.Trim(), Urls = urls };
    }
    public async Task<WorkspaceItem?> EditItemAsync(WorkspaceItem item)
    {
        var content = Ui.Stack();
        var name = new TextBox { Header = "Name", Text = item.Name, MaxLength = 120 };
        var target = new TextBox { Header = item.Kind == ItemKind.Website ? "Links, one per line" : "Path",
            Text = item.Kind == ItemKind.Website ? string.Join("\n", item.Urls) : item.Path ?? "",
            AcceptsReturn = item.Kind == ItemKind.Website, TextWrapping = TextWrapping.Wrap };
        var arguments = new TextBox { Header = "Arguments", Text = item.App?.Arguments ?? "" };
        content.Children.Add(name);
        if (item.Kind == ItemKind.App)
        {
            content.Children.Add(Ui.Text(item.App?.ExePath is { Length: > 0 } exe ? exe : item.App?.Aumid ?? "App unavailable", secondary: true));
            content.Children.Add(arguments);
        }
        else content.Children.Add(target);
        // Use the same system picker as Add. The dialog stays open and prefilled while browsing.
        if (item.Kind is ItemKind.File or ItemKind.Folder)
        {
            var browse = new Button { Content = "Browse…" };
            browse.Click += async (_, _) =>
            {
                try { if (await PickPathAsync(item.Kind) is { } path) target.Text = path; }
                catch (Exception) { /* Keep the existing path; the user can type or retry. */ }
            };
            content.Children.Add(browse);
        }
        var error = Ui.Text("", secondary: true); content.Children.Add(error);
        var dialog = Dialog("Edit item", content, "Save changes");
        WorkspaceItem? edited = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            string? problem = WorkspaceItemEdits.Apply(item, name.Text, target.Text, arguments.Text,
                File.Exists, Directory.Exists, out var candidate);
            if (problem is not null) { args.Cancel = true; error.Text = problem; }
            else edited = candidate;
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? edited : null;
    }
    public async Task<CaptureCandidate?> PickRunningAsync(IReadOnlyList<CaptureCandidate> candidates)
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 320 };
        int? currentDesktop = DesktopLabels.CurrentDesktopIndex(candidates.Select(c => c.Window));
        foreach (var candidate in candidates)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            var icon = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            IconSource.SetPath(icon, candidate.Window.ExePath);
            row.Children.Add(icon);
            var text = Ui.Stack(2); text.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(Ui.Text(candidate.Item.Name, "BodyStrongTextBlockStyle"));
            string exe = candidate.Window.ExeName + ".exe";
            string desktop = DesktopLabels.For(candidate.Window, currentDesktop);
            text.Children.Add(Ui.Text(desktop.Length == 0 ? exe : $"{exe} · {desktop}", secondary: true));
            row.Children.Add(text); list.Items.Add(row);
        }
        var dialog = Dialog("Pick a running app", list, "Add app");
        dialog.IsPrimaryButtonEnabled = false;
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedIndex >= 0;
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? candidates[list.SelectedIndex] : null;
    }
    public async Task ShowReportAsync(RestoreReport report)
    {
        var content = Ui.Stack();
        foreach (var item in report.Items)
        {
            content.Children.Add(Ui.Text(item.Name + " · " + RestoreReportText.Outcome(item.Outcome), "BodyStrongTextBlockStyle"));
            content.Children.Add(Ui.Text(RestoreReportText.Detail(item), secondary: true));
        }
        if (report.Context.Count > 0)
        {
            content.Children.Add(Ui.Text("Context", "BodyStrongTextBlockStyle"));
            foreach (var note in report.Context) content.Children.Add(Ui.Text(note.Text, secondary: true));
        }
        await Dialog("Workspace results", Ui.Scroll(content), close: "Close").ShowAsync();
    }
}
