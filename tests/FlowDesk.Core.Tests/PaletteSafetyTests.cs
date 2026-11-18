using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteSafetyTests
{
    [Theory]
    [InlineData(PaletteInvocation.Enter)]
    [InlineData(PaletteInvocation.Pointer)]
    [InlineData(PaletteInvocation.ModifiedEnter)]
    public async Task FirstInvocationAlwaysPromptsWithoutRunningOrHiding(PaletteInvocation input)
    {
        int runs = 0, hides = 0, prompts = 0;
        var action = new PaletteAction("Restart", () => { runs++; return Task.CompletedTask; }, NeedsConfirm: true);
        var item = Item(action);
        var executor = new PaletteActionExecutor(() => TimeSpan.Zero);
        Assert.Equal(PaletteExecution.Prompted, await executor.InvokeAsync(item, 0, input, _ => prompts++, _ => hides++));
        Assert.Equal(1, prompts);
        Assert.Equal(0, runs);
        Assert.Equal(0, hides);
        Assert.True(executor.IsPending);
    }

    [Theory]
    [InlineData(false, PaletteInvocation.Enter)]
    [InlineData(true, PaletteInvocation.Enter)]
    [InlineData(false, PaletteInvocation.Pointer)]
    [InlineData(true, PaletteInvocation.Pointer)]
    public async Task ResultAndPanelBothUseGuardedTargetAndSecondDeliberateInput(bool panel, PaletteInvocation input)
    {
        int runs = 0, hides = 0;
        var item = Item(new("Restart", () => { runs++; return Task.CompletedTask; }, NeedsConfirm: true));
        var state = panel ? new PaletteActionPanel<CommandItem>(item, new object(), 2, "3 results") : null;
        var executor = new PaletteActionExecutor(() => TimeSpan.Zero);
        Task<PaletteExecution> Invoke() => executor.InvokeAsync(state?.Target ?? item, 0, input, _ => { }, _ => hides++);
        Assert.Equal(PaletteExecution.Prompted, await Invoke());
        Assert.Equal(0, runs);
        Assert.Equal(PaletteExecution.Executed, await Invoke());
        Assert.Equal(1, runs);
        Assert.Equal(1, hides);
        Assert.False(executor.IsPending);
        Assert.Equal(PaletteExecution.Prompted, await Invoke());
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task ModifiedEnterNeverConfirmsAndGestureChangeRestartsPrompt()
    {
        int runs = 0;
        var item = Item(new("Delete", () => { runs++; return Task.CompletedTask; }, NeedsConfirm: true));
        var executor = new PaletteActionExecutor(() => TimeSpan.Zero);
        Task<PaletteExecution> Invoke(PaletteInvocation input) => executor.InvokeAsync(item, 0, input, _ => { }, _ => { });
        Assert.Equal(PaletteExecution.Prompted, await Invoke(PaletteInvocation.ModifiedEnter));
        Assert.Equal(PaletteExecution.Prompted, await Invoke(PaletteInvocation.ModifiedEnter));
        Assert.Equal(PaletteExecution.Prompted, await Invoke(PaletteInvocation.Enter));
        Assert.Equal(PaletteExecution.Prompted, await Invoke(PaletteInvocation.Pointer));
        Assert.Equal(0, runs);
    }

    [Theory]
    [InlineData("key")]
    [InlineData("selection")]
    [InlineData("text")]
    [InlineData("hide")]
    [InlineData("timeout")]
    public async Task CancellationAndDeadlineNeverExecute(string reason)
    {
        TimeSpan now = TimeSpan.Zero;
        int runs = 0;
        var item = Item(new("Restart", () => { runs++; return Task.CompletedTask; }, NeedsConfirm: true));
        var executor = new PaletteActionExecutor(() => now);
        Task<PaletteExecution> Invoke() => executor.InvokeAsync(item, 0, PaletteInvocation.Enter, _ => { }, _ => { });
        await Invoke();
        if (reason == "timeout") now = PaletteConfirmation.Duration;
        else executor.Cancel();
        Assert.Equal(PaletteExecution.Prompted, await Invoke());
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task PromptFailureCannotAuthorizeTheNextActivation()
    {
        int runs = 0;
        var item = Item(new("Restart", () => { runs++; return Task.CompletedTask; }, NeedsConfirm: true));
        var executor = new PaletteActionExecutor(() => TimeSpan.Zero);
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.InvokeAsync(item, 0, PaletteInvocation.Enter,
            _ => throw new InvalidOperationException(), _ => { }));
        Assert.False(executor.IsPending);
        Assert.Equal(PaletteExecution.Prompted, await executor.InvokeAsync(item, 0, PaletteInvocation.Enter, _ => { }, _ => { }));
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task ChangedTargetOrActionCannotReuseConfirmation()
    {
        int runs = 0;
        PaletteAction Action() => new("Restart", () => { runs++; return Task.CompletedTask; }, NeedsConfirm: true);
        var item = Item(Action());
        var executor = new PaletteActionExecutor(() => TimeSpan.Zero);
        Task<PaletteExecution> Invoke(CommandItem target, int index = 0) => executor.InvokeAsync(target, index, PaletteInvocation.Enter, _ => { }, _ => { });
        await Invoke(item);
        Assert.Equal(PaletteExecution.Prompted, await Invoke(item with { Id = "other" }));
        Assert.Equal(PaletteExecution.Invalid, await Invoke(item, 1));
        Assert.Equal(PaletteExecution.Prompted, await Invoke(item));
        Assert.Equal(PaletteExecution.Prompted, await Invoke(item with { NamedActions = [Action() with { Title = "Changed" }] }));
        Assert.Equal(0, runs);
    }

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData("setup.py")]
    [InlineData("readme.md")]
    [InlineData("v1.2")]
    [InlineData("photo.jpg")]
    [InlineData("archive.zip")]
    [InlineData("script.sh")]
    [InlineData("main.rs")]
    [InlineData("test.pl")]
    public void FileNamesDoNotBecomeAddressesInMixedMode(string text)
    {
        Assert.Null(WebSearch.Address(text));
        var source = new WebCommandSource(() => new(true, true, WebEngine.Google, ""), _ => Task.CompletedTask);
        Assert.DoesNotContain(source.Query(text), m => m.Item.Id == "web:open");
    }

    [Theory]
    [InlineData("github.com/x", "https://github.com/x")]
    [InlineData("https://localhost:5000", "https://localhost:5000/")]
    [InlineData("www.example.org", "https://www.example.org/")]
    [InlineData("docs.example.dev", "https://docs.example.dev/")]
    public void RecognizableAddressesRemainAvailable(string query, string expected) => Assert.Equal(expected, WebSearch.Address(query)?.AbsoluteUri);

    [Fact]
    public void ExplicitWebModeAllowsUncommonDnsSuffixAndMixedAddressLosesToExactApp()
    {
        Assert.NotNull(WebSearch.Address("readme.md", webMode: true));
        const string text = "github.com";
        var item = new CommandItem("app", text, "", CommandKind.App, "", () => Task.CompletedTask);
        var usage = new PaletteUsageStore("unused.json");
        var source = new WebCommandSource(() => new(true, true, WebEngine.Google, ""), _ => Task.CompletedTask);
        var search = new CommandSearch([new SnapshotCommandSource("app", () => [item]), source], usage, (_, _) => { });
        Assert.Equal(new[] { "app", "web:open" }, search.Query(text).Select(m => m.Item.Id));
    }

    private static CommandItem Item(PaletteAction action) => new("system:Restart", "Restart", "", CommandKind.System, "",
        () => throw new InvalidOperationException("Legacy Primary must never be used"), NamedActions: [action]);
}
