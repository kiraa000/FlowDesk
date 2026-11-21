using FlowDesk.Core.Model;
using FlowDesk.Core.Palette;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class CustomCommandEditor : UserControl
{
    private readonly string id;
    private readonly IReadOnlyList<CustomCommandDefinition> others;
    public CustomCommandEditor(CustomCommandDefinition? existing, IReadOnlyList<CustomCommandDefinition> others)
    {
        this.others = others; id = existing?.Id ?? Guid.NewGuid().ToString("N");
        InitializeComponent();
        CommandNameBox.Text = existing?.Name ?? ""; KeywordsBox.Text = existing?.Keywords ?? "";
        KindBox.SelectedIndex = (int)(existing?.Kind ?? CustomTargetKind.Url);
        TargetBox.Text = existing?.Target ?? ""; ArgumentsBox.Text = existing?.Arguments ?? "";
        AdminBox.IsChecked = existing?.RunAsAdmin ?? false; IconBox.Text = existing?.Icon ?? "";
        foreach (var box in new[] { CommandNameBox, KeywordsBox, TargetBox, ArgumentsBox, IconBox }) box.TextChanged += (_, _) => Validate();
        KindBox.SelectionChanged += (_, _) => { UpdateBrowseTarget(); Validate(); };
        AdminBox.Checked += (_, _) => Validate(); AdminBox.Unchecked += (_, _) => Validate();
        UpdateBrowseTarget();
    }
    public CustomCommandDefinition Value => new(id, CommandNameBox.Text.Trim(), KeywordsBox.Text.Trim(),
        (CustomTargetKind)KindBox.SelectedIndex, TargetBox.Text.Trim(), ArgumentsBox.Text, AdminBox.IsChecked == true, IconBox.Text.Trim());
    public bool Validate()
    {
        ValidationText.Text = CustomCommandRules.Validate(Value, others) ?? "";
        return ValidationText.Text.Length == 0;
    }
    public void ShowError(string error) => ValidationText.Text = error;
    private void UpdateBrowseTarget() => BrowseTargetButton.Visibility =
        (CustomTargetKind)KindBox.SelectedIndex is CustomTargetKind.File or CustomTargetKind.Folder or CustomTargetKind.Program
            ? Visibility.Visible : Visibility.Collapsed;
    private async void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        var kind = (CustomTargetKind)KindBox.SelectedIndex;
        if (kind is not (CustomTargetKind.File or CustomTargetKind.Folder or CustomTargetKind.Program)) return;
        try
        {
            string? path = await App.ViewModels.Ui.PickPathAsync(kind switch
            {
                CustomTargetKind.Folder => ItemKind.Folder,
                CustomTargetKind.Program => ItemKind.App,
                _ => ItemKind.File,
            });
            if (string.IsNullOrEmpty(path)) return;
            TargetBox.Text = path;
            TargetBox.Focus(FocusState.Programmatic);
        }
        catch (Exception ex) { ShowError("The picker could not open: " + ex.Message); }
    }
    private async void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await App.ViewModels.Ui.PickPathAsync(ItemKind.File);
            if (string.IsNullOrEmpty(path)) return;
            IconBox.Text = path;
            IconBox.Focus(FocusState.Programmatic);
        }
        catch (Exception ex) { ShowError("The picker could not open: " + ex.Message); }
    }
}
