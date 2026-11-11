using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.Core.Model;

namespace FlowDesk.App.ViewModels;

public partial class WorkspaceViewModel : ObservableObject
{
    internal Workspace Model { get; }
    private readonly AppViewModels owner;
    private bool initializing = true;
    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string Description { get; set; }
    [ObservableProperty] public partial string Color { get; set; }
    [ObservableProperty] public partial string Glyph { get; set; }
    [ObservableProperty] public partial bool IsOpening { get; set; }
    [ObservableProperty] public partial string OpeningText { get; set; } = "";
    public ObservableCollection<ItemViewModel> Items { get; } = [];
    public string Summary
    {
        get
        {
            var age = DateTimeOffset.Now - Model.LastUsed;
            string used = age is null ? "Never opened" : age.Value.TotalMinutes < 1 ? "Last used just now"
                : age.Value.TotalHours < 1 ? $"Last used {(int)age.Value.TotalMinutes} min ago"
                : age.Value.TotalDays < 1 ? $"Last used {(int)age.Value.TotalHours} hours ago"
                : $"Last used {(int)age.Value.TotalDays} days ago";
            string active = owner.Services.Switcher.ActiveWorkspaceId == Model.Id ? " · Active" : "";
            return $"{Items.Count} items · {used} · {ColorLabel}{active}";
        }
    }
    public string ColorLabel => string.IsNullOrEmpty(Color) ? ""
        : char.ToUpperInvariant(Color[0]) + Color[1..];
    public bool IsEmpty => Items.Count == 0;
    public WorkspaceViewModel(Workspace model, AppViewModels owner)
    {
        Model = model; this.owner = owner;
        Name = model.Name; Description = model.Description; Color = model.Color;
        try { Glyph = char.ConvertFromUtf32(Convert.ToInt32(model.Glyph, 16)); }
        catch { Glyph = "\uE8A1"; }
        ReloadItems(); initializing = false;
    }
    partial void OnNameChanged(string value)
    {
        if (!initializing && !string.IsNullOrWhiteSpace(value)) { Model.Name = value.Trim(); owner.Services.Changed(); }
    }
    public void CommitName() { if (string.IsNullOrWhiteSpace(Name)) Name = Model.Name; }
    partial void OnDescriptionChanged(string value)
    {
        if (initializing) return;
        Model.Description = value; owner.Services.Changed();
    }
    partial void OnColorChanged(string value)
    {
        if (initializing) return;
        Model.Color = value; owner.Services.Changed();
        OnPropertyChanged(nameof(ColorLabel)); RefreshSummary();
    }
    partial void OnGlyphChanged(string value)
    {
        if (initializing) return;
        if (!string.IsNullOrEmpty(value)) Model.Glyph = char.ConvertToUtf32(value, 0).ToString("X");
        owner.Services.Changed();
    }
    internal void RefreshSummary() => OnPropertyChanged(nameof(Summary));
    internal void ReloadItems()
    {
        Items.Clear();
        foreach (var item in Model.Items) Items.Add(new(item, this, owner));
        RefreshSummary(); OnPropertyChanged(nameof(IsEmpty));
    }
    [RelayCommand] private Task OpenAsync() => owner.OpenAsync(this);
    [RelayCommand] private void Edit() { CommitName(); owner.Edit(this); }
    [RelayCommand] private Task SavePositionsAsync() => owner.SavePositionsAsync(this);
    [RelayCommand] private void Reset()
    {
        owner.Services.Switcher.ResetSession(Model.Id);
        owner.ShowStatus("The next open will use the saved layout instead of the session positions.");
    }
    [RelayCommand] private void Duplicate()
    {
        CommitName(); var copy = Model.Clone(); copy.Id = WorkspaceItem.NewId(); copy.Name += " copy";
        copy.LastUsed = null; copy.CreatedAt = DateTimeOffset.Now;
        foreach (var item in copy.Items) item.Id = WorkspaceItem.NewId();
        owner.Add(copy);
    }
    [RelayCommand] private Task DeleteAsync() => owner.DeleteAsync(this);
}

public partial class ItemViewModel(WorkspaceItem model, WorkspaceViewModel workspace, AppViewModels owner)
    : ObservableObject
{
    public string Name => model.Name;
    public string Detail => model.Kind == ItemKind.Website ? string.Join("\n", model.Urls)
        : model.Path ?? model.App?.ExePath ?? model.App?.Aumid ?? "Program unavailable";
    public string ExePath => model.App?.ExePath ?? (model.Kind == ItemKind.File ? model.Path : null) ?? "";
    public string Glyph => ItemDisplay.Glyph(model.Kind);
    public string Position => ItemDisplay.Position(model);
    [RelayCommand]
    private async Task RemoveAsync() => await owner.ExecuteAsync(async () =>
    {
        if (!await owner.Ui.ConfirmAsync("Remove item?",
            $"Remove {Name} from this workspace? The original will not be deleted.", "Remove item")) return;
        workspace.Model.Items.Remove(model); workspace.ReloadItems(); owner.Services.Changed();
    });
}

internal static class ItemDisplay
{
    public static string Glyph(ItemKind kind) => kind switch
    {
        ItemKind.Website => "\uE774", ItemKind.Folder => "\uE8B7", ItemKind.File => "\uE8A5", _ => "\uE7C3"
    };
    public static string Position(WorkspaceItem item) => item.Window is { } w
        ? $"{w.MonitorDevice.Replace(@"\\.\DISPLAY", "Monitor ")} · {w.Show} · {w.Rect.Width} × {w.Rect.Height}"
        : "Position chosen by the app";
}
