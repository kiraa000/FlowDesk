using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowDesk.App.Services;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.ViewModels;

public partial class SettingsViewModel
{
    public ObservableCollection<CustomCommandDefinition> CustomCommands { get; } = [];
    [ObservableProperty] public partial string CustomCommandsError { get; set; } = "";
    private void RefreshCustomCommands()
    {
        CustomCommands.Clear();
        var settings = owner.Services.Settings.CustomCommands ?? new();
        foreach (var entry in settings.Entries ?? []) if (entry is not null) CustomCommands.Add(entry);
        CustomCommandsError = settings.Version == 1 ? "" : "These commands use a newer format and cannot be changed in this version.";
    }
    internal bool SaveCustomCommand(CustomCommandDefinition command)
    {
        var settings = owner.Services.Settings.CustomCommands ?? new();
        if (settings.Version != 1) return false;
        string? error = CustomCommandRules.Validate(command, settings.Entries ?? [], path =>
            command.Kind == CustomTargetKind.Folder ? Directory.Exists(path) : File.Exists(path));
        if (error is not null) { CustomCommandsError = error; return false; }
        owner.Services.Settings.CustomCommands = new() { Entries = [.. (settings.Entries ?? []).Where(c => c.Id != command.Id), command] };
        owner.Services.Changed(); RefreshCustomCommands(); PaletteSourcesChanged?.Invoke(); return true;
    }
    internal void DeleteCustomCommand(string id)
    {
        var settings = owner.Services.Settings.CustomCommands ?? new();
        if (settings.Version != 1) return;
        owner.Services.Settings.CustomCommands = new() { Entries = [.. (settings.Entries ?? []).Where(c => c.Id != id)] };
        owner.Services.Changed(); RefreshCustomCommands(); PaletteSourcesChanged?.Invoke();
    }
    internal async Task TestCustomCommandAsync(CustomCommandDefinition command, string query)
    {
        CustomCommandsError = "";
        if (CustomCommandRules.Validate(command, CustomCommands) is { } error) { CustomCommandsError = error; return; }
        try { await CustomCommandExecutor.RunAsync(CustomCommandRules.Build(command, query)); }
        catch (Exception) { CustomCommandsError = "That command could not run. Check the target and arguments."; }
    }
}
