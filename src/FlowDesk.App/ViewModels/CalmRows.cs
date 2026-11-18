using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.Core.Calm;

namespace FlowDesk.App.ViewModels;

public sealed record CalmGroup(string Title, ObservableCollection<CalmRow> Rows);

/// <summary>Keyed reconciliation keeps bound controls alive across check-ups, writes and undo.</summary>
public static class CalmRowRefresh
{
    public static void Apply(ObservableCollection<CalmGroup> groups, ObservableCollection<CalmStartupRow> apps,
        IReadOnlyList<CalmReading> readings, IReadOnlyList<StartupEntry> startup, Func<string, bool> hasUndo,
        Func<CalmRow, bool, Task> change, Func<CalmStartupRow, bool, Task> changeStartup, Func<bool> canInteract)
    {
        var desiredGroups = readings.GroupBy(r => r.Setting.Group).ToArray();
        Reconcile(groups, desiredGroups, g => g.Title, g => GroupTitle(g.Key),
            g => new CalmGroup(GroupTitle(g.Key), []), (group, fresh) =>
                Reconcile(group.Rows, fresh.ToArray(), r => r.Reading.Setting.Id, r => r.Setting.Id,
                    r => new CalmRow(r, hasUndo(r.Setting.Id), change, canInteract),
                    (row, r) => row.Update(r, hasUndo(r.Setting.Id))));
        Reconcile(apps, startup, r => r.Entry.Id, r => r.Id,
            r => new CalmStartupRow(r, hasUndo(r.Id), changeStartup, canInteract),
            (row, r) => row.Update(r, hasUndo(r.Id)));
    }
    private static string GroupTitle(string key) => key switch
    { "Start" => "Start menu & suggestions", "Lock" => "Lock screen", "Explorer" => "File Explorer", _ => key };

    private static void Reconcile<T, TReading>(ObservableCollection<T> rows, IReadOnlyList<TReading> fresh,
        Func<T, string> key, Func<TReading, string> freshKey, Func<TReading, T> create, Action<T, TReading> update)
    {
        for (int index = 0; index < fresh.Count; index++)
        {
            string id = freshKey(fresh[index]);
            int existing = index;
            while (existing < rows.Count && key(rows[existing]) != id) existing++;
            if (existing == rows.Count) rows.Insert(index, create(fresh[index]));
            else if (existing != index) rows.Move(existing, index);
            update(rows[index], fresh[index]);
        }
        while (rows.Count > fresh.Count) rows.RemoveAt(rows.Count - 1);
    }
}
public sealed class CalmRow : ObservableObject
{
    public CalmReading Reading { get; private set; }
    public string Title => Reading.Setting.Title;
    public string Description => Reading.Setting.Description;
    public string Caption => Reading.State switch
    {
        CalmState.Custom => "Custom value — not changed by Calm all",
        CalmState.Managed => "Set by a policy on this PC",
        CalmState.Unavailable => "Unavailable — access denied",
        _ => (Reading.UsesDefault ? "Windows default · " : "") + Reading.Setting.Effect
    };
    public bool CanChange => Reading.State is CalmState.Calm or CalmState.Noisy || IsSearch && Reading.State == CalmState.Custom;
    public bool CanToggle => !IsSearch && Reading.State is CalmState.Calm or CalmState.Noisy;
    public bool HasUndo { get; private set; }
    public bool IsSearch => Reading.Setting.SearchChoice;
    public bool IsToggle => !IsSearch;
    public string UndoName => "Undo " + Title;
    public bool CanApplySearch => CanChange && SelectedSearch is >= 0 and <= 2;
    private bool isOn;
    // Set the backing field during construction. Binding initial values must never execute a write.
    public bool IsOn
    {
        get => isOn;
        set
        {
            if (isOn == value) return;
            if (!canInteract()) { OnPropertyChanged(); return; }
            if (CanToggle && SetProperty(ref isOn, value)) ChangeCommand.Execute(null);
        }
    }
    private int selectedSearch;
    public int SelectedSearch
    {
        get => selectedSearch;
        set
        {
            if (selectedSearch == value) return;
            if (!canInteract()) { OnPropertyChanged(); return; }
            if (SetProperty(ref selectedSearch, value)) OnPropertyChanged(nameof(CanApplySearch));
        }
    }
    public IAsyncRelayCommand ChangeCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    private readonly Func<bool> canInteract;
    public CalmRow(CalmReading reading, bool undo, Func<CalmRow, bool, Task> change, Func<bool>? canInteract = null)
    {
        this.canInteract = canInteract ?? (() => true);
        Reading = reading; HasUndo = undo; isOn = reading.State == CalmState.Noisy; selectedSearch = reading.Choice ?? -1;
        ChangeCommand = new AsyncRelayCommand(() => change(this, false), AsyncRelayCommandOptions.AllowConcurrentExecutions);
        UndoCommand = new AsyncRelayCommand(() => change(this, true), AsyncRelayCommandOptions.AllowConcurrentExecutions);
    }
    public void Update(CalmReading reading, bool undo)
    {
        if (reading.Setting.Id != Reading.Setting.Id) throw new ArgumentException("Cannot change row identity.", nameof(reading));
        Reading = reading; HasUndo = undo;
        // Never use the two-way setter here: a read/undo must not write back to Windows.
        isOn = reading.State == CalmState.Noisy; selectedSearch = reading.Choice ?? -1;
        OnPropertyChanged(string.Empty);
    }
}
public sealed class CalmStartupRow : ObservableObject
{
    public StartupEntry Entry { get; private set; }
    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Caption => Entry.Enabled is null ? "Custom startup state — open Windows Settings" : "Takes effect at next sign-in";
    public bool CanChange => Entry.Enabled is not null;
    public bool HasUndo { get; private set; }
    public string UndoName => "Undo " + Name;
    private bool isOn;
    public bool IsOn
    {
        get => isOn;
        set
        {
            if (isOn == value) return;
            if (!canInteract()) { OnPropertyChanged(); return; }
            if (CanChange && SetProperty(ref isOn, value)) ChangeCommand.Execute(null);
        }
    }
    public IAsyncRelayCommand ChangeCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    private readonly Func<bool> canInteract;
    public CalmStartupRow(StartupEntry entry, bool undo, Func<CalmStartupRow, bool, Task> change, Func<bool>? canInteract = null)
    {
        this.canInteract = canInteract ?? (() => true);
        Entry = entry; HasUndo = undo; isOn = entry.Enabled == true;
        ChangeCommand = new AsyncRelayCommand(() => change(this, false), AsyncRelayCommandOptions.AllowConcurrentExecutions);
        UndoCommand = new AsyncRelayCommand(() => change(this, true), AsyncRelayCommandOptions.AllowConcurrentExecutions);
    }
    public void Update(StartupEntry entry, bool undo)
    {
        if (entry.Id != Entry.Id) throw new ArgumentException("Cannot change row identity.", nameof(entry));
        Entry = entry; HasUndo = undo; isOn = entry.Enabled == true;
        OnPropertyChanged(string.Empty);
    }
}
