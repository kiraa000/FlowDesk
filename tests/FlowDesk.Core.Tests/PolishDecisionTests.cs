using System.Text.Json;
using FlowDesk.Core.Model;
using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PolishDecisionTests
{
    [Theory] [InlineData(ContextPreset.None)] [InlineData(ContextPreset.Study)]
    [InlineData(ContextPreset.Coding)] [InlineData(ContextPreset.Gaming)]
    public void PresetsAreRecognizedAfterReload(ContextPreset preset)
    {
        var reloaded = JsonSerializer.Deserialize<WorkspaceContext>(JsonSerializer.Serialize(ContextPresets.Create(preset)))!;
        Assert.Equal((int)preset, ContextPresets.Match(reloaded));
    }
    [Fact] public void CustomizedDurationStrictnessOrAudioIsNotAPreset()
    {
        var context = ContextPresets.Create(ContextPreset.Study);
        context.FocusMinutes = 90; Assert.Equal(-1, ContextPresets.Match(context));
        context.FocusMinutes = 50; context.FocusStrictness = FocusStrictness.Strict;
        Assert.Equal(-1, ContextPresets.Match(context));
        context.FocusStrictness = FocusStrictness.Normal; context.SoundOutput = new() { EndpointId = "usb" };
        Assert.Equal(-1, ContextPresets.Match(context));
    }
    [Fact] public void SecondaryHintNamesTheActualKeyboardActionOnly()
    {
        Func<Task> run = () => Task.CompletedTask;
        var item = new CommandItem("app", "App", "", CommandKind.App, "", run, run);
        Assert.Equal("Ctrl+Enter · Run as administrator", item.SecondaryHint);
        Assert.Equal("", (item with { Secondary = null }).SecondaryHint);
        Assert.Equal("Ctrl+Enter · Close window", (item with { NamedActions = [new("Switch", run, Shortcut: "Enter"), new("Close window", run, Shortcut: "Ctrl+Enter")] }).SecondaryHint);
        Assert.Equal("", (item with { NamedActions = [new("Open", run), new("Menu action", run)] }).SecondaryHint);
    }
}
