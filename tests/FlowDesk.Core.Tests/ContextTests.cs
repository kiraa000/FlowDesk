using System.Text.Json;
using FlowDesk.Core.Contexts;
using FlowDesk.Core.Model;
using FlowDesk.Core.Storage;

namespace FlowDesk.Core.Tests;

public class ContextTests
{
    private static ContextState State(bool running = false, bool strict = false, bool canStart = true,
        bool? on = false, bool available = true, AudioOutputState? audio = null) =>
        new(running, strict, canStart, new(available, on), audio ?? new(true, [], null));
    [Fact] public void Old_and_explicit_null_context_are_safe_defaults()
    {
        foreach (var json in new[] { "{\"Name\":\"Old\"}", "{\"Context\":null}" })
        {
            var workspace = JsonSerializer.Deserialize<Workspace>(json, JsonStore.Options)!;
            Assert.True(workspace.Context.IsEmpty);
            Assert.Equal(25, workspace.Context.FocusMinutes);
            Assert.Equal(FocusStrictness.Normal, workspace.Context.FocusStrictness);
            Assert.Empty(ContextPlanner.Plan(workspace.Context, State()));
        }
    }
    [Fact] public void Clone_and_duplicate_pattern_deep_copy_context_and_device()
    {
        var original = new Workspace { Context = new() { Focus = ContextFocus.Start, FocusMinutes = 90,
            FocusStrictness = FocusStrictness.Strict, DoNotDisturb = ContextToggle.Off,
            SoundOutput = new() { Name = "Headphones", EndpointId = "id", StableId = "stable" } } };
        var copy = original.Clone(); copy.Id = WorkspaceItem.NewId(); copy.Name += " copy";
        Assert.NotSame(original.Context, copy.Context);
        Assert.NotSame(original.Context.SoundOutput, copy.Context.SoundOutput);
        Assert.Equal(90, copy.Context.FocusMinutes); Assert.Equal(FocusStrictness.Strict, copy.Context.FocusStrictness);
        Assert.Equal(ContextToggle.Off, copy.Context.DoNotDisturb);
        copy.Context.SoundOutput!.Name = "Other"; copy.Context.Focus = ContextFocus.End;
        Assert.Equal("Headphones", original.Context.SoundOutput!.Name);
        Assert.Equal(ContextFocus.Start, original.Context.Focus);
    }
    [Theory]
    [InlineData(ContextPreset.None, ContextFocus.Unchanged, 25, ContextToggle.Unchanged)]
    [InlineData(ContextPreset.Study, ContextFocus.Start, 50, ContextToggle.On)]
    [InlineData(ContextPreset.Coding, ContextFocus.Unchanged, 25, ContextToggle.On)]
    [InlineData(ContextPreset.Gaming, ContextFocus.End, 25, ContextToggle.On)]
    public void Presets_are_independent_values(ContextPreset preset, ContextFocus focus, int minutes, ContextToggle dnd)
    {
        var context = ContextPresets.Create(preset);
        Assert.Equal(focus, context.Focus); Assert.Equal(minutes, context.FocusMinutes);
        Assert.Equal(dnd, context.DoNotDisturb); Assert.Null(context.SoundOutput);
        Assert.Equal(FocusStrictness.Normal, context.FocusStrictness);
        Assert.NotSame(context, ContextPresets.Create(preset));
    }
    [Fact] public void End_is_before_restore_other_steps_after_in_order()
    {
        var context = new WorkspaceContext { Focus = ContextFocus.End, DoNotDisturb = ContextToggle.On,
            SoundOutput = new() { Name = "Speakers", EndpointId = "speaker" } };
        var plan = ContextPlanner.Plan(context, State(running: true,
            audio: new(true, [context.SoundOutput], "other")));
        Assert.Equal(new[] { ContextStepKind.FocusEnd, ContextStepKind.DoNotDisturb, ContextStepKind.SoundOutput }, plan.Select(s => s.Kind));
        Assert.True(plan[0].BeforeRestore); Assert.All(plan.Skip(1), s => Assert.False(s.BeforeRestore));
        context.Focus = ContextFocus.Start;
        plan = ContextPlanner.Plan(context, State(audio: new(true, [context.SoundOutput], "other")));
        Assert.Equal(ContextStepKind.FocusStart, plan[^1].Kind);
        Assert.All(plan, s => Assert.False(s.BeforeRestore));
    }
    [Theory]
    [InlineData(ContextFocus.Start, true, false, true, "Focus session already running")]
    [InlineData(ContextFocus.Start, true, true, false, "Focus session already running")]
    [InlineData(ContextFocus.Start, false, false, false, "Focus not started: no blocked apps")]
    [InlineData(ContextFocus.End, true, true, true, "Strict focus session kept")]
    public void Focus_skip_rules(ContextFocus action, bool running, bool strict, bool canStart, string reason)
    {
        var step = Assert.Single(ContextPlanner.Plan(new() { Focus = action }, State(running, strict, canStart)));
        Assert.Equal(reason, step.SkipReason);
    }
    [Fact] public void End_when_idle_is_silent() => Assert.Empty(ContextPlanner.Plan(new() { Focus = ContextFocus.End }, State()));
    [Theory] [InlineData(ContextToggle.On, true)] [InlineData(ContextToggle.Off, false)]
    public void Dnd_matching_or_unavailable_is_silent(ContextToggle wanted, bool current)
    {
        Assert.Empty(ContextPlanner.Plan(new() { DoNotDisturb = wanted }, State(on: current)));
        Assert.Empty(ContextPlanner.Plan(new() { DoNotDisturb = wanted }, State(on: !current, available: false)));
    }
    [Fact] public void Dnd_unknown_state_requires_write() => Assert.Single(ContextPlanner.Plan(new() { DoNotDisturb = ContextToggle.On }, State(on: null)));
    [Fact] public void Stable_id_wins_then_endpoint_fallback_and_case_is_opaque()
    {
        var saved = new ContextAudioDevice { EndpointId = "old", StableId = "Stable" };
        ContextAudioDevice endpoint = new() { EndpointId = "old", StableId = "different" };
        ContextAudioDevice stable = new() { EndpointId = "new", StableId = "Stable" };
        Assert.Same(stable, ContextPlanner.Resolve(saved, [endpoint, stable]));
        Assert.Same(endpoint, ContextPlanner.Resolve(saved, [endpoint]));
        Assert.Null(ContextPlanner.Resolve(saved, [new() { EndpointId = "new", StableId = "stable" }]));
    }
    [Fact] public void Audio_already_default_is_silent_missing_has_note()
    {
        var device = new ContextAudioDevice { EndpointId = "id", Name = "Headphones" };
        Assert.Empty(ContextPlanner.Plan(new() { SoundOutput = device }, State(audio: new(true, [device], "id"))));
        Assert.Empty(ContextPlanner.Plan(new() { SoundOutput = device }, State(audio: new(false, [device], "id"))));
        var step = Assert.Single(ContextPlanner.Plan(new() { SoundOutput = device }, State()));
        Assert.Equal("Headphones is not connected; sound output not changed", step.SkipReason);
    }
    [Fact] public void Context_round_trip_and_invalid_minutes()
    {
        var context = ContextPresets.Create(ContextPreset.Study); context.FocusMinutes = -5;
        Assert.Equal(25, context.FocusMinutes);
        context.SoundOutput = new() { EndpointId = "id", StableId = "Stable", Name = "Headphones" };
        var clone = JsonSerializer.Deserialize<WorkspaceContext>(JsonSerializer.Serialize(context, JsonStore.Options), JsonStore.Options)!;
        Assert.Equal("Stable", clone.SoundOutput!.StableId);
        Assert.Equal(ContextToggle.On, clone.DoNotDisturb);
        Assert.Equal("Focus 25 min · Do not disturb · Sound: Headphones", ContextPlanner.Summary(clone));
        Assert.Equal("", ContextPlanner.Summary(new()));
    }
    [Fact] public void Unknown_numeric_modes_do_not_change_the_pc()
    {
        var context = JsonSerializer.Deserialize<WorkspaceContext>("{\"Focus\":999,\"DoNotDisturb\":999,\"FocusStrictness\":999}", JsonStore.Options)!;
        Assert.True(context.IsEmpty); Assert.Equal(FocusStrictness.Normal, context.FocusStrictness);
        Assert.Empty(ContextPlanner.Plan(context, State()));
    }
}
