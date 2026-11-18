using FlowDesk.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace FlowDesk.App.Services;

/// <summary>Presentation only: collects the user's export/import choices. All file work happens outside.</summary>
internal sealed class SettingsTransferDialogs(FrameworkElement root)
{
    private const string PrivacyText = "Workspaces contain file paths and web addresses. API keys are never exported.";
    private const string BodyStyle = "BodyTextBlockStyle";
    private const string CaptionStyle = "CaptionTextBlockStyle";

    public async Task<TransferSelection?> ExportAsync()
    {
        var content = new StackPanel { Spacing = 8, MaxWidth = 440 };
        var checks = new List<CheckBox>();
        var workspaces = AddSection(content, checks, "Workspaces and layouts");
        var focus = AddSection(content, checks, "Focus settings");
        var settings = AddSection(content, checks, "Settings and shortcuts");
        var customCommands = AddSection(content, checks, "Custom palette commands");
        content.Children.Add(Ui.Text(PrivacyText, CaptionStyle, secondary: true));
        content.Children.Add(Ui.Text("Review command arguments and web addresses before sharing.", CaptionStyle, secondary: true));

        var dialog = CreateDialog("Export FlowDesk settings", content, "Export");
        void Update() => dialog.IsPrimaryButtonEnabled = checks.Any(check => check.IsChecked == true);
        Subscribe(checks, Update);
        Update();
        FocusFirst(dialog, checks);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return new TransferSelection(
            Workspaces: workspaces.IsChecked == true,
            Focus: focus.IsChecked == true,
            Settings: settings.IsChecked == true,
            CustomCommands: customCommands.IsChecked == true,
            Replace: false);
    }

    public async Task<TransferSelection?> ImportAsync(TransferPreview preview, int existingWorkspaceCount,
        int existingCustomCommandCount)
    {
        var content = new StackPanel { Spacing = 8, MaxWidth = 440 };
        var checks = new List<CheckBox>();
        var byId = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
        CheckBox? workspaces = null;

        foreach (var section in preview.Sections)
        {
            string? id = KnownId(section.Id);
            if (id is null) continue;
            string label = string.IsNullOrWhiteSpace(section.Summary) ? Label(id) : section.Summary;
            bool broken = !string.IsNullOrWhiteSpace(section.Error);
            var check = AddSection(content, checks, label, isChecked: !broken, isEnabled: !broken);
            if (!broken) byId[id] = check;
            if (id == "workspaces" && !broken) workspaces = check;
            if (broken) content.Children.Add(Caution(section.Error!));
        }

        RadioButtons? mode = null;
        TextBlock? caution = null;
        if (workspaces is not null)
        {
            mode = new RadioButtons { Header = "Workspaces", MaxColumns = 1 };
            mode.Items.Add("Add to my workspaces");
            mode.Items.Add("Replace my workspaces");
            mode.SelectedIndex = 0;
            caution = Caution("");
            caution.Visibility = Visibility.Collapsed;
            AutomationProperties.SetLiveSetting(caution, AutomationLiveSetting.Polite);
            content.Children.Add(mode);
            content.Children.Add(caution);
        }

        var dialog = CreateDialog("Import FlowDesk settings", content, "Import");
        void Update()
        {
            dialog.IsPrimaryButtonEnabled = checks.Any(check => check.IsEnabled && check.IsChecked == true);
            bool useWorkspaces = workspaces is { IsChecked: true, IsEnabled: true };
            if (mode is not null) mode.Visibility = useWorkspaces ? Visibility.Visible : Visibility.Collapsed;
            if (caution is null) return;
            bool replace = useWorkspaces && mode?.SelectedIndex == 1;
            if (!replace) { caution.Text = ""; caution.Visibility = Visibility.Collapsed; return; }
            string text = $"This removes your {existingWorkspaceCount} existing workspace{(existingWorkspaceCount == 1 ? "" : "s")}. Your apps and files are not deleted.";
            if (byId.TryGetValue("customCommands", out var commands) && commands.IsChecked == true)
                text += $" This also replaces your {existingCustomCommandCount} custom command{(existingCustomCommandCount == 1 ? "" : "s")}.";
            caution.Text = text;
            caution.Visibility = Visibility.Visible;
        }
        Subscribe(checks, Update);
        if (mode is not null) mode.SelectionChanged += (_, _) => Update();
        Update();
        FocusFirst(dialog, checks);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        bool Selected(string id) => byId.TryGetValue(id, out var check) && check.IsChecked == true;
        bool replaceWorkspaces = workspaces is { IsChecked: true } && mode?.SelectedIndex == 1;
        return new TransferSelection(
            Workspaces: Selected("workspaces"),
            Focus: Selected("focus"),
            Settings: Selected("settings"),
            CustomCommands: Selected("customCommands"),
            Replace: replaceWorkspaces);
    }

    private ContentDialog CreateDialog(string title, UIElement content, string primary)
    {
        var scroll = Ui.Scroll(content);
        scroll.MaxHeight = 420;
        return new()
        {
            XamlRoot = root.XamlRoot,
            RequestedTheme = root.RequestedTheme,
            Title = title,
            Content = scroll,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
    }

    private static CheckBox AddSection(StackPanel content, ICollection<CheckBox> checks, string label,
        bool isChecked = true, bool isEnabled = true)
    {
        var text = Ui.Text(label, BodyStyle);
        var check = new CheckBox { Content = text, IsChecked = isChecked, IsEnabled = isEnabled, MinHeight = 32 };
        AutomationProperties.SetName(check, label);
        checks.Add(check);
        content.Children.Add(check);
        return check;
    }

    private static void Subscribe(IEnumerable<CheckBox> checks, Action update)
    {
        foreach (var check in checks)
        {
            check.Checked += (_, _) => update();
            check.Unchecked += (_, _) => update();
        }
    }

    private static void FocusFirst(ContentDialog dialog, IReadOnlyList<CheckBox> checks)
    {
        var first = checks.FirstOrDefault(check => check.IsEnabled);
        if (first is not null) dialog.Opened += (_, _) => first.Focus(FocusState.Keyboard);
    }

    private static TextBlock Caution(string text)
    {
        var block = Text(text, CaptionStyle, "TextFillColorPrimaryBrush");
        block.Margin = new Thickness(28, 0, 0, 0);
        return block;
    }

    private static TextBlock Text(string text, string style, string? brush = null)
    {
        // Built through XamlReader so styles and brushes stay live theme resources.
        var markup = "<TextBlock xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' "
            + $"Style='{{StaticResource {style}}}'"
            + (brush is null ? "" : $" Foreground='{{ThemeResource {brush}}}'")
            + " TextWrapping='Wrap' />";
        var block = (TextBlock)XamlReader.Load(markup);
        block.Text = text;
        return block;
    }

    private static string? KnownId(string id) => id is "workspaces" or "focus" or "settings" or "customCommands" ? id : null;

    private static string Label(string id) => id switch
    {
        "workspaces" => "Workspaces and layouts",
        "focus" => "Focus settings",
        "settings" => "Settings and shortcuts",
        _ => "Custom palette commands"
    };
}
