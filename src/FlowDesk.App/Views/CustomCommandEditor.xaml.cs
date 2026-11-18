using FlowDesk.Core.Palette;
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
        KindBox.SelectionChanged += (_, _) => Validate();
        AdminBox.Checked += (_, _) => Validate(); AdminBox.Unchecked += (_, _) => Validate();
    }
    public CustomCommandDefinition Value => new(id, CommandNameBox.Text.Trim(), KeywordsBox.Text.Trim(),
        (CustomTargetKind)KindBox.SelectedIndex, TargetBox.Text.Trim(), ArgumentsBox.Text, AdminBox.IsChecked == true, IconBox.Text.Trim());
    public bool Validate()
    {
        ValidationText.Text = CustomCommandRules.Validate(Value, others) ?? "";
        return ValidationText.Text.Length == 0;
    }
    public void ShowError(string error) => ValidationText.Text = error;
}
