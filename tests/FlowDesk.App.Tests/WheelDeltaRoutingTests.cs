namespace FlowDesk.App.Tests;

// Structural hook check only. Physical touchpad and Start-menu suppression require a live pass.
public sealed class WheelDeltaRoutingTests
{
    [Fact]
    public void Hook_passes_signed_delta_and_arms_Win_release_for_swallowed_partials_before_dispatch()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", "Services", "HotkeyHost.cs"));
        var start = source.IndexOf("var decision = _wheel.Accept", StringComparison.Ordinal);
        var end = source.IndexOf("private nint KeyboardHookProc", start, StringComparison.Ordinal);
        var wheel = source[start..end];
        Assert.Contains("Environment.TickCount64, delta)", wheel);
        Assert.Contains("if (modifiers.Win)", wheel);
        Assert.DoesNotContain("decision.Accepted", wheel[..wheel.IndexOf("_keyboard.AcceptWinGesture()", StringComparison.Ordinal)]);
        Assert.Contains("if (decision.Accepted) QueueWheel(decision.Gesture, delta > 0, policy)", wheel);
        Assert.True(wheel.IndexOf("_keyboard.AcceptWinGesture()", StringComparison.Ordinal) <
            wheel.IndexOf("if (decision.Accepted) QueueWheel", StringComparison.Ordinal));
        Assert.Contains("return 1;", wheel);
    }
}
