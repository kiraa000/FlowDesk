using FlowDesk.Core.Model;

namespace FlowDesk.Core.Contexts;

public sealed record AudioOutputState(bool CanSetDefault, IReadOnlyList<ContextAudioDevice> Devices, string? DefaultEndpointId);
public sealed record DoNotDisturbState(bool IsAvailable, bool? IsOn, string? ProfileId = null);
public interface IAudioOutput
{
    Task<AudioOutputState> ReadAsync();
    Task<bool> SetDefaultAsync(string endpointId);
}
public interface IDoNotDisturb
{
    Task<DoNotDisturbState> ReadAsync();
    Task<bool> SetAsync(bool on);
}
public enum ContextStepKind { FocusEnd, DoNotDisturb, SoundOutput, FocusStart }
public sealed record ContextStep(ContextStepKind Kind, bool BeforeRestore, string? SkipReason = null, string? EndpointId = null);
public sealed record ContextState(bool FocusRunning, bool FocusStrict, bool CanStartFocus,
    DoNotDisturbState Dnd, AudioOutputState Audio);
public sealed record ContextNote(string Text, bool IsFailure = false);

public static class ContextPlanner
{
    public static ContextAudioDevice? Resolve(ContextAudioDevice saved, IEnumerable<ContextAudioDevice> devices)
    {
        var list = devices.ToArray();
        return (!string.IsNullOrWhiteSpace(saved.StableId) ? list.FirstOrDefault(d =>
            string.Equals(d.StableId, saved.StableId, StringComparison.Ordinal)) : null)
            ?? list.FirstOrDefault(d => string.Equals(d.EndpointId, saved.EndpointId, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<ContextStep> Plan(WorkspaceContext context, ContextState state)
    {
        List<ContextStep> steps = [];
        if (context.Focus == ContextFocus.End && state.FocusRunning)
            steps.Add(new(ContextStepKind.FocusEnd, true, state.FocusStrict ? "Strict focus session kept" : null));
        if (context.DoNotDisturb != ContextToggle.Unchanged && state.Dnd.IsAvailable &&
            state.Dnd.IsOn != (context.DoNotDisturb == ContextToggle.On))
            steps.Add(new(ContextStepKind.DoNotDisturb, false));
        if (context.SoundOutput is { } saved)
        {
            var device = Resolve(saved, state.Audio.Devices);
            if (device is null) steps.Add(new(ContextStepKind.SoundOutput, false,
                $"{saved.Name} is not connected; sound output not changed"));
            else if (!state.Audio.CanSetDefault) steps.Add(new(ContextStepKind.SoundOutput, false, "Could not change sound output"));
            else if (!string.Equals(device.EndpointId, state.Audio.DefaultEndpointId, StringComparison.OrdinalIgnoreCase))
                steps.Add(new(ContextStepKind.SoundOutput, false, EndpointId: device.EndpointId));
        }
        if (context.Focus == ContextFocus.Start)
            steps.Add(new(ContextStepKind.FocusStart, false, state.FocusRunning ? "Focus session already running"
                : !state.CanStartFocus ? "Focus not started: no blocked apps" : null));
        return steps;
    }

    public static string Summary(WorkspaceContext context)
    {
        List<string> parts = [];
        if (context.Focus == ContextFocus.Start) parts.Add($"Focus {context.FocusMinutes} min");
        if (context.Focus == ContextFocus.End) parts.Add("End focus");
        if (context.DoNotDisturb != ContextToggle.Unchanged)
            parts.Add(context.DoNotDisturb == ContextToggle.On ? "Do not disturb" : "Do not disturb off");
        if (context.SoundOutput is { } device) parts.Add(device.Name);
        return string.Join(" · ", parts);
    }
}
