using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class FocusReturnPolicyTests
{
    [Fact] public void UsableInvokerWinsWithoutMovingToOtherControls()
    {
        var attempts = new List<string>();
        Assert.True(FocusReturnPolicy.TryRestore(new[] { "Install", "Open", "Website" }, candidate => { attempts.Add(candidate); return true; }));
        Assert.Equal(new[] { "Install" }, attempts);
    }
    [Theory]
    [InlineData("Open")]
    [InlineData("Website")]
    public void DisabledOrHiddenInvokerFallsBackWithinCard(string available)
    {
        var attempts = new List<string>();
        Assert.True(FocusReturnPolicy.TryRestore(new[] { "Install", "Open", "Website" }, candidate =>
        { attempts.Add(candidate); return candidate == available; }));
        Assert.Equal(available == "Open" ? new[] { "Install", "Open" } : new[] { "Install", "Open", "Website" }, attempts);
    }
    [Fact] public void NoUsableCardControlDoesNotFocusUnrelatedNavigation()
    {
        Assert.False(FocusReturnPolicy.TryRestore(new[] { "Install", "Open", "Website" }, _ => false));
        Assert.False(FocusReturnPolicy.TryRestore(Array.Empty<string>(), _ => throw new Exception("No target")));
    }
}
