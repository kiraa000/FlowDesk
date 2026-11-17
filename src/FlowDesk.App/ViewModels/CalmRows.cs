using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.Core.Calm;

namespace FlowDesk.App.ViewModels;

public sealed record CalmGroup(string Title, IReadOnlyList<CalmRow> Rows);
public sealed class CalmRow : ObservableObject
{
    public CalmReading Reading { get; }
    public string Title => Reading.Setting.Title;
    public string Description => Reading.Setting.Description;
    public string Caption => Reading.State switch
    {
        CalmState.Custom => "Custom value — not changed by Calm all",
        CalmState.Managed => "Set by a policy on this PC",
        CalmState.Unavailable => "Unavailable — access denied",
        _ => (Reading.State == CalmState.Calm ? "Off" : "On") + (Reading.UsesDefault ? " · Windows default" : "") + " · " + Reading.Setting.Effect
    };
    public bool CanChange => Reading.State is CalmState.Calm or CalmState.Noisy || IsSearch && Reading.State == CalmState.Custom;
    public bool CanToggle => !IsSearch && Reading.State is CalmState.Calm or CalmState.Noisy;
    public bool HasUndo { get; }
    public bool IsSearch => Reading.Setting.SearchChoice;
    public bool IsToggle => !IsSearch;
    public string UndoName => "Undo " + Title;
    public bool CanApplySearch => CanChange && SelectedSearch is >= 0 and <= 2;
    private bool isOn;
    // Set the backing field during construction. Binding initial values must never execute a write.
    public bool IsOn
    {
        get => isOn;
        set { if (CanToggle && SetProperty(ref isOn, value)) ChangeCommand.Execute(null); }
    }
    private int selectedSearch;
    public int SelectedSearch { get => selectedSearch; set { if (SetProperty(ref selectedSearch, value)) OnPropertyChanged(nameof(CanApplySearch)); } }
    public IAsyncRelayCommand ChangeCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    public CalmRow(CalmReading reading, bool undo, Func<CalmRow, bool, Task> change)
    {
        Reading = reading; HasUndo = undo; isOn = reading.State == CalmState.Noisy; selectedSearch = reading.Choice ?? -1;
        ChangeCommand = new AsyncRelayCommand(() => change(this, false));
        UndoCommand = new AsyncRelayCommand(() => change(this, true));
    }
}
public sealed class CalmStartupRow : ObservableObject
{
    public StartupEntry Entry { get; }
    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Caption => Entry.Enabled is null ? "Custom startup state — open Windows Settings" : "Takes effect at next sign-in";
    public bool CanChange => Entry.Enabled is not null;
    public bool HasUndo { get; }
    public string UndoName => "Undo " + Name;
    private bool isOn;
    public bool IsOn { get => isOn; set { if (CanChange && SetProperty(ref isOn, value)) ChangeCommand.Execute(null); } }
    public IAsyncRelayCommand ChangeCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    public CalmStartupRow(StartupEntry entry, bool undo, Func<CalmStartupRow, bool, Task> change)
    {
        Entry = entry; HasUndo = undo; isOn = entry.Enabled == true;
        ChangeCommand = new AsyncRelayCommand(() => change(this, false));
        UndoCommand = new AsyncRelayCommand(() => change(this, true));
    }
}
