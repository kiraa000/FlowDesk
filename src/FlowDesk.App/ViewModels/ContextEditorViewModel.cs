using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.Core.Contexts;
using FlowDesk.Core.Focus;
using FlowDesk.Core.Model;

namespace FlowDesk.App.ViewModels;

public sealed record ContextAudioChoice(string Name, ContextAudioDevice? Device);

/// <summary>Dispatcher-owned editing. Device refresh never changes a persisted selection.</summary>
public sealed class ContextEditorViewModel : ObservableObject
{
    private readonly Workspace workspace;
    private readonly IAudioOutput audio;
    private readonly IDoNotDisturb dnd;
    private readonly Func<FocusSettings> focusSettings;
    private readonly Action changed;
    private int presetIndex;
    private int refreshVersion;
    private bool refreshing;
    private IReadOnlyList<ContextAudioDevice> activeDevices = [];
    public ContextEditorViewModel(Workspace workspace, IAudioOutput audio, IDoNotDisturb dnd,
        Func<FocusSettings> focusSettings, Action changed)
    {
        this.workspace = workspace; this.audio = audio; this.dnd = dnd;
        this.focusSettings = focusSettings; this.changed = changed;
        presetIndex = ContextPresets.Match(workspace.Context);
        ClearSoundOutputCommand = new RelayCommand(() => SelectedAudio = AudioChoices[0]);
        RebuildAudio([]);
    }
    public int[] DurationChoices { get; } = [25, 50, 90];
    public int PresetIndex
    {
        get => presetIndex;
        set
        {
            if (value < 0 || value > 3 || !SetProperty(ref presetIndex, value)) return;
            workspace.Context = ContextPresets.Create((ContextPreset)value);
            RebuildAudio(activeDevices);
            NotifyFields(); changed();
        }
    }
    public int FocusIndex { get => (int)workspace.Context.Focus; set { if (value is < 0 or > 2 || value == FocusIndex) return; workspace.Context.Focus = (ContextFocus)value; Edited(); } }
    public int FocusMinutes { get => workspace.Context.FocusMinutes; set { if (!DurationChoices.Contains(value) || value == FocusMinutes) return; workspace.Context.FocusMinutes = value; Edited(); } }
    public int StrictnessIndex { get => (int)workspace.Context.FocusStrictness; set { if (value is < 0 or > 2 || value == StrictnessIndex) return; workspace.Context.FocusStrictness = (FocusStrictness)value; Edited(); } }
    public int DndIndex { get => (int)workspace.Context.DoNotDisturb; set { if (value is < 0 or > 2 || value == DndIndex) return; workspace.Context.DoNotDisturb = (ContextToggle)value; Edited(); } }
    public bool FocusStartVisible => workspace.Context.Focus == ContextFocus.Start;
    public bool DndAvailable { get; private set; } = true;
    public bool DndUnavailable => !DndAvailable;
    public bool AudioAvailable { get; private set; } = true;
    public bool AudioUnavailable => !AudioAvailable;
    public bool HasSoundOutput => workspace.Context.SoundOutput is not null;
    public IRelayCommand ClearSoundOutputCommand { get; }
    public ObservableCollection<ContextAudioChoice> AudioChoices { get; } = [];
    private ContextAudioChoice? selectedAudio;
    public ContextAudioChoice? SelectedAudio
    {
        get => selectedAudio;
        set
        {
            if (refreshing || value is null || !AudioChoices.Contains(value) || !SetProperty(ref selectedAudio, value)) return;
            workspace.Context.SoundOutput = value.Device?.Clone(); Edited();
        }
    }
    public string Caution => BuildCaution(workspace, focusSettings());
    public bool HasCaution => Caution.Length > 0;
    public static string BuildCaution(Workspace workspace, FocusSettings settings)
    {
        if (workspace.Context.Focus != ContextFocus.Start) return "";
        var names = workspace.Items.Where(i => i.App is { } app &&
            FocusWatcher.WouldBlockApp(app.ExePath, app.Aumid, settings, out _)).Select(i => i.Name).Distinct();
        return string.Join("\n", names.Select(n => workspace.Context.FocusStrictness == FocusStrictness.Gentle
            ? $"{n} is blocked during focus; Gentle mode will show a reminder."
            : $"{n} is blocked during focus and will be closed."));
    }
    private void Edited()
    {
        presetIndex = ContextPresets.Match(workspace.Context); NotifyFields(); changed();
    }
    private void NotifyFields()
    {
        foreach (string name in new[] { nameof(PresetIndex), nameof(FocusIndex), nameof(FocusMinutes), nameof(StrictnessIndex),
            nameof(DndIndex), nameof(FocusStartVisible), nameof(Caution), nameof(HasCaution), nameof(HasSoundOutput) }) OnPropertyChanged(name);
    }
    internal void RefreshCaution() { OnPropertyChanged(nameof(Caution)); OnPropertyChanged(nameof(HasCaution)); }
    public async Task RefreshAsync()
    {
        int version = ++refreshVersion;
        var audioTask = ReadAudioAsync(); var dndTask = ReadDndAsync();
        var state = await audioTask; var quiet = await dndTask;
        if (version != refreshVersion) return;
        AudioAvailable = state.CanSetDefault; DndAvailable = quiet.IsAvailable;
        activeDevices = state.Devices.Select(d => d.Clone()).ToArray();
        RebuildAudio(activeDevices);
        foreach (string name in new[] { nameof(AudioAvailable), nameof(AudioUnavailable), nameof(DndAvailable), nameof(DndUnavailable) }) OnPropertyChanged(name);
        RefreshCaution();
    }
    private async Task<AudioOutputState> ReadAudioAsync() { try { return await audio.ReadAsync(); } catch { return new(false, [], null); } }
    private async Task<DoNotDisturbState> ReadDndAsync() { try { return await dnd.ReadAsync(); } catch { return new(false, null); } }
    private void RebuildAudio(IReadOnlyList<ContextAudioDevice> devices)
    {
        refreshing = true;
        try
        {
            AudioChoices.Clear(); AudioChoices.Add(new("Don't change", null));
            foreach (var device in devices) AudioChoices.Add(new(device.Name, device.Clone()));
            var saved = workspace.Context.SoundOutput;
            var resolved = saved is null ? null : ContextPlanner.Resolve(saved, devices);
            selectedAudio = resolved is null ? AudioChoices[0] : AudioChoices.First(c => c.Device?.EndpointId == resolved.EndpointId);
            if (saved is not null && resolved is null)
            {
                selectedAudio = new(saved.Name + " (not connected)", saved.Clone()); AudioChoices.Add(selectedAudio);
            }
            OnPropertyChanged(nameof(SelectedAudio));
        }
        finally { refreshing = false; }
    }
}
