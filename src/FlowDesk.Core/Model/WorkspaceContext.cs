namespace FlowDesk.Core.Model;

public enum ContextFocus { Unchanged, Start, End }
public enum ContextToggle { Unchanged, On, Off }

public sealed class ContextAudioDevice
{
    public string EndpointId { get; set; } = "";
    public string? StableId { get; set; }
    public string Name { get; set; } = "";
    public ContextAudioDevice Clone() => (ContextAudioDevice)MemberwiseClone();
}

public sealed class WorkspaceContext
{
    private ContextFocus focus;
    public ContextFocus Focus { get => focus; set => focus = Enum.IsDefined(value) ? value : ContextFocus.Unchanged; }
    private int minutes = 25;
    public int FocusMinutes { get => minutes; set => minutes = FocusSettings.DurationChoices.Contains(value) ? value : 25; }
    private FocusStrictness strictness = FocusStrictness.Normal;
    public FocusStrictness FocusStrictness { get => strictness; set => strictness = Enum.IsDefined(value) ? value : FocusStrictness.Normal; }
    private ContextToggle doNotDisturb;
    public ContextToggle DoNotDisturb { get => doNotDisturb; set => doNotDisturb = Enum.IsDefined(value) ? value : ContextToggle.Unchanged; }
    public ContextAudioDevice? SoundOutput { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Focus == ContextFocus.Unchanged && DoNotDisturb == ContextToggle.Unchanged && SoundOutput is null;
    public WorkspaceContext Clone() => new() { Focus = Focus, FocusMinutes = FocusMinutes,
        FocusStrictness = FocusStrictness, DoNotDisturb = DoNotDisturb, SoundOutput = SoundOutput?.Clone() };
}

public enum ContextPreset { None, Study, Coding, Gaming }
public static class ContextPresets
{
    public static WorkspaceContext Create(ContextPreset preset) => preset switch
    {
        ContextPreset.Study => new() { Focus = ContextFocus.Start, FocusMinutes = 50, DoNotDisturb = ContextToggle.On },
        ContextPreset.Coding => new() { DoNotDisturb = ContextToggle.On },
        ContextPreset.Gaming => new() { Focus = ContextFocus.End, DoNotDisturb = ContextToggle.On },
        _ => new(),
    };
}
