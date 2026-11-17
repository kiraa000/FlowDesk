using FlowDesk.App.Services;
using FlowDesk.App.ViewModels;
using FlowDesk.Core.Contexts;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;

namespace FlowDesk.App.Tests;

public class ContextAppTests
{
    private sealed class Audio(List<string> calls) : IAudioOutput
    {
        public AudioOutputState State = new(true, [new() { Name = "Headphones", EndpointId = "id", StableId = "stable" }], "old");
        public bool Success = true;
        public Task<AudioOutputState> ReadAsync() { calls.Add("audio-read"); return Task.FromResult(State); }
        public Task<bool> SetDefaultAsync(string endpointId) { calls.Add("audio-set:" + endpointId); return Task.FromResult(Success); }
    }
    private sealed class Dnd(List<string> calls) : IDoNotDisturb
    {
        public DoNotDisturbState State = new(true, false);
        public bool Throws;
        public Task<DoNotDisturbState> ReadAsync() { calls.Add("dnd-read"); if (Throws) throw new InvalidOperationException(); return Task.FromResult(State); }
        public Task<bool> SetAsync(bool on) { calls.Add("dnd-set:" + on); return Task.FromResult(true); }
    }
    private sealed class Focus(List<string> calls) : IContextFocus
    {
        public Task<ContextFocusState> ReadContextStateAsync() => Task.FromResult(new ContextFocusState(false, false, true));
        public Task<ContextNote?> ApplyContextFocusAsync(WorkspaceContext context)
        { calls.Add("focus:" + context.Focus); return Task.FromResult<ContextNote?>(new("Focus " + context.Focus)); }
    }
    [Fact] public async Task Empty_context_makes_no_service_calls()
    {
        List<string> calls = []; var apply = new ContextApplier(new Audio(calls), new Dnd(calls), new Focus(calls), _ => { });
        Assert.Empty(await apply.ApplyAsync(new(), true)); Assert.Empty(await apply.ApplyAsync(new(), false)); Assert.Empty(calls);
    }
    [Fact] public async Task End_before_restore_and_native_steps_after_then_start()
    {
        List<string> calls = []; var audio = new Audio(calls);
        var apply = new ContextApplier(audio, new Dnd(calls), new Focus(calls), _ => { });
        var context = new WorkspaceContext { Focus = ContextFocus.End, DoNotDisturb = ContextToggle.On, SoundOutput = audio.State.Devices[0] };
        await apply.ApplyAsync(context, true); calls.Add("restore"); await apply.ApplyAsync(context, false);
        Assert.Equal(new[] { "focus:End", "restore", "dnd-read", "dnd-set:True", "audio-read", "audio-set:id" }, calls);
        calls.Clear(); context.Focus = ContextFocus.Start;
        await apply.ApplyAsync(context, true); Assert.Empty(calls);
        await apply.ApplyAsync(context, false); Assert.Equal("focus:Start", calls[^1]);
    }
    [Fact] public async Task One_exception_does_not_block_later_steps_and_is_logged()
    {
        List<string> calls = []; int logs = 0;
        var apply = new ContextApplier(new Audio(calls), new Dnd(calls) { Throws = true }, new Focus(calls), _ => logs++);
        var notes = await apply.ApplyAsync(new() { DoNotDisturb = ContextToggle.On, Focus = ContextFocus.Start }, false);
        Assert.True(notes[0].IsFailure); Assert.False(notes[^1].IsFailure); Assert.Equal(1, logs);
        Assert.Contains("focus:Start", calls);
    }
    [Fact] public async Task Unavailable_dnd_is_silent_audio_failure_warns()
    {
        List<string> calls = []; var audio = new Audio(calls) { Success = false };
        var apply = new ContextApplier(audio, new Dnd(calls) { State = new(false, null) }, new Focus(calls), _ => { });
        var notes = await apply.ApplyAsync(new() { DoNotDisturb = ContextToggle.On, SoundOutput = audio.State.Devices[0] }, false);
        Assert.Equal("Could not change sound output", Assert.Single(notes).Text); Assert.True(notes[0].IsFailure);
        Assert.DoesNotContain("dnd-set:True", calls);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Context_status_matches_report_and_only_failure_warns(bool failed)
    {
        var report = new RestoreReport([], TimeSpan.Zero) { Context = [new("Focus session already running", failed)] };
        var (text, warning) = RestoreReportText.Summary("Study", report);
        Assert.EndsWith(" · Focus session already running", text); Assert.Equal(failed, warning);
    }
    [Fact] public async Task Editor_refresh_preserves_saved_missing_device_and_never_persists()
    {
        List<string> calls = []; int saves = 0;
        var workspace = new Workspace { Context = new() { SoundOutput = new() { Name = "USB", EndpointId = "missing" } } };
        var editor = new ContextEditorViewModel(workspace, new Audio(calls), new Dnd(calls) { State = new(false, null) }, () => new(), () => saves++);
        await editor.RefreshAsync();
        Assert.Equal("USB (not connected)", editor.SelectedAudio!.Name); Assert.Equal("missing", workspace.Context.SoundOutput!.EndpointId);
        Assert.True(editor.DndUnavailable); Assert.False(editor.DndAvailable); Assert.True(editor.AudioAvailable); Assert.Equal(0, saves);
        editor.SelectedAudio = editor.AudioChoices[0]; Assert.Null(workspace.Context.SoundOutput); Assert.Equal(1, saves);
        editor.PresetIndex = 1;
        Assert.DoesNotContain(editor.AudioChoices, c => c.Device?.EndpointId == "missing");
    }
    [Fact] public async Task Editor_unavailable_audio_and_presets_are_editable()
    {
        List<string> calls = []; int saves = 0; var workspace = new Workspace();
        var editor = new ContextEditorViewModel(workspace, new Audio(calls) { State = new(false, [], null) }, new Dnd(calls), () => new(), () => saves++);
        await editor.RefreshAsync(); Assert.True(editor.AudioUnavailable); Assert.False(editor.AudioAvailable);
        editor.PresetIndex = 1; Assert.True(editor.FocusStartVisible); Assert.Equal(50, editor.FocusMinutes); Assert.Equal(1, editor.DndIndex);
        editor.FocusMinutes = 90; Assert.Equal(-1, editor.PresetIndex); Assert.Equal(90, workspace.Context.FocusMinutes);
        editor.PresetIndex = 0; Assert.True(workspace.Context.IsEmpty); Assert.Equal(3, saves);
    }
    [Fact] public void Caution_matches_allowed_blockall_gentle_and_system_exemptions()
    {
        var workspace = new Workspace { Context = new() { Focus = ContextFocus.Start }, Items =
            [new() { Name = "Game", App = new() { ExePath = @"D:\game.exe" } }, new() { Name = "Explorer", App = new() { ExePath = @"D:\explorer.exe" } }] };
        var settings = new FocusSettings { BlockAllExceptAllowed = true };
        Assert.Equal("Game is blocked during focus and will be closed.", ContextEditorViewModel.BuildCaution(workspace, settings));
        workspace.Context.FocusStrictness = FocusStrictness.Gentle;
        Assert.Contains("Gentle mode", ContextEditorViewModel.BuildCaution(workspace, settings));
        settings.Allowed.Add(new() { ExeName = "game" }); Assert.Equal("", ContextEditorViewModel.BuildCaution(workspace, settings));
    }
    [Fact] public async Task Unsupported_audio_can_be_cleared_without_any_native_write()
    {
        List<string> calls = []; int saves = 0;
        var workspace = new Workspace { Context = new() { SoundOutput = new() { EndpointId = "id", Name = "Headphones" } } };
        var editor = new ContextEditorViewModel(workspace, new Audio(calls) { State = new(false, [], null) }, new Dnd(calls), () => new(), () => saves++);
        await editor.RefreshAsync(); Assert.True(editor.HasSoundOutput); Assert.True(editor.AudioUnavailable);
        editor.ClearSoundOutputCommand.Execute(null);
        Assert.False(editor.HasSoundOutput); Assert.Null(workspace.Context.SoundOutput); Assert.Equal(1, saves);
        Assert.DoesNotContain(calls, c => c.StartsWith("audio-set:", StringComparison.Ordinal));
    }
}
