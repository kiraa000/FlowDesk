using FlowDesk.Core.Advanced;
using FlowDesk.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Services;

public sealed class AdvancedDialogs(FrameworkElement root)
{
    internal Func<DialogFocusReturn?>? TakeReturnFocus { get; set; }
    private async Task<bool> ConfirmAsync(string title, UIElement content, string primary)
    {
        var returnFocus = TakeReturnFocus?.Invoke();
        var dialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot, RequestedTheme = root.RequestedTheme,
            Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        // DefaultButton controls Enter, but focusable content can still win initial focus. Explicitly
        // focus the native template's CloseButton once it exists; links stay in the normal Tab order.
        dialog.Opened += (_, _) => DialogFocusReturn.FindNamed<Button>(dialog, "CloseButton")?.Focus(FocusState.Keyboard);
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        finally { returnFocus?.RestoreAfterDialog(); }
    }
    public Task<bool> EnableAsync() => ConfirmAsync("Turn on Advanced mode?", Ui.Text(
        "Tools here can change Windows itself. Some need administrator rights.\n\n" +
        "These are third-party programs. FlowDesk installs them only from winget's official source and never bundles them.\n\n" +
        "Chris Titus WinUtil downloads and runs a PowerShell script from the internet as administrator.\n\n" +
        "FlowDesk does not undo changes made by these tools."), "Turn on");
    public Task<bool> InstallAsync(AdvancedTool tool)
    {
        var content = Ui.Stack();
        content.Children.Add(Ui.Text(tool.Name, "BodyStrongTextBlockStyle"));
        content.Children.Add(Ui.Text($"Publisher: {tool.Publisher}\nSource: winget (official)\n\nThe installer may show a Windows administrator (UAC) prompt.\n\nClicking Install accepts the package and source agreements."));
        content.Children.Add(new HyperlinkButton { Content = "Official website and license information", NavigateUri = new Uri(tool.Website) });
        return ConfirmAsync("Install " + tool.Name + "?", content, "Install");
    }
    public Task<bool> WinUtilAsync()
    {
        var content = Ui.Stack();
        content.Children.Add(new FontIcon { Glyph = "\uE7BA", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), HorizontalAlignment = HorizontalAlignment.Left });
        content.Children.Add(Ui.Text("WinUtil changes Windows itself. It can remove apps, change services and apply tweaks. FlowDesk cannot undo its changes."));
        content.Children.Add(Ui.Text("This downloads and runs a PowerShell script from the internet as administrator:"));
        content.Children.Add(Ui.Text("irm \"https://christitus.com/win\" | iex", "BodyStrongTextBlockStyle"));
        content.Children.Add(Ui.Text("Create a restore point first."));
        var protection = new HyperlinkButton { Content = "Open System Protection" };
        protection.Click += (_, _) =>
        {
            try { AdvancedToolService.OpenLink(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SystemPropertiesProtection.exe")); }
            catch (Exception ex) { App.ViewModels.ShowStatus("Could not open System Protection: " + ex.Message, ViewModels.StatusKind.Warning); }
        };
        content.Children.Add(protection);
        return ConfirmAsync("Warning: run Chris Titus WinUtil?", content, "Run as administrator");
    }
    public Task<bool> WaitBeforeExitAsync() => ConfirmAsync("An installation is running", Ui.Text(
        "FlowDesk will wait for winget to finish before exiting so the installer is not interrupted. You can also cancel to keep FlowDesk open."), "Wait, then exit");
}
