using FlowDesk.Core.Contexts;
using FlowDesk.Core.Model;

namespace FlowDesk.App.Services;

public sealed record ContextFocusState(bool Running, bool Strict, bool CanStart);
public interface IContextFocus
{
    Task<ContextFocusState> ReadContextStateAsync();
    Task<ContextNote?> ApplyContextFocusAsync(WorkspaceContext context);
}

/// <summary>One opening owns its notes. Each phase samples fresh state; no startup or leave hooks.</summary>
public sealed class ContextApplier(IAudioOutput audio, IDoNotDisturb dnd, IContextFocus focus, Action<Exception> log)
{
    public async Task<IReadOnlyList<ContextNote>> ApplyAsync(WorkspaceContext context, bool beforeRestore)
    {
        List<ContextNote> notes = [];
        if (context.IsEmpty) return notes;
        // Execute focus independently: a device enumeration failure must never suppress a focus action.
        if (beforeRestore && context.Focus == ContextFocus.End)
            await Safe(async () => await focus.ApplyContextFocusAsync(context), "Could not end focus session");
        if (beforeRestore) return notes;

        if (context.DoNotDisturb != ContextToggle.Unchanged)
            await Safe(async () =>
            {
                var state = await dnd.ReadAsync();
                var steps = ContextPlanner.Plan(new() { DoNotDisturb = context.DoNotDisturb },
                    new(false, false, false, state, new(false, [], null)));
                if (steps.Count == 0) return null;
                bool on = context.DoNotDisturb == ContextToggle.On;
                return await dnd.SetAsync(on) ? new(on ? "Do not disturb on" : "Do not disturb off")
                    : new(on ? "Could not turn on Do not disturb" : "Could not turn off Do not disturb", true);
            }, context.DoNotDisturb == ContextToggle.On ? "Could not turn on Do not disturb" : "Could not turn off Do not disturb");
        if (context.SoundOutput is { } saved)
            await Safe(async () =>
            {
                var state = await audio.ReadAsync();
                var steps = ContextPlanner.Plan(new() { SoundOutput = saved },
                    new(false, false, false, new(false, null), state));
                if (steps.Count == 0) return null;
                var step = steps[0];
                if (step.SkipReason is { } reason) return new(reason, reason == "Could not change sound output");
                return await audio.SetDefaultAsync(step.EndpointId!) ? new("Sound: " + saved.Name)
                    : new("Could not change sound output", true);
            }, "Could not change sound output");
        if (context.Focus == ContextFocus.Start)
            await Safe(async () => await focus.ApplyContextFocusAsync(context), "Could not start focus session");
        return notes;

        async Task Safe(Func<Task<ContextNote?>> action, string failure)
        {
            try { if (await action() is { } note) notes.Add(note); }
            catch (Exception ex) { log(ex); notes.Add(new(failure, true)); }
        }
    }
}
