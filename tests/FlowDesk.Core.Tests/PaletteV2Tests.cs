using System.Globalization;
using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteV2Tests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static Task Noop() => Task.CompletedTask;

    [Theory]
    [InlineData(CommandKind.Workspace, "Open", "Edit in FlowDesk")]
    [InlineData(CommandKind.App, "Open", "Run as administrator")]
    [InlineData(CommandKind.File, "Open", "Show in Explorer")]
    [InlineData(CommandKind.Clipboard, "Paste", "Copy")]
    public void LegacyActionsKeepOrderAndNameBothKeys(CommandKind kind, string first, string second)
    {
        var item = new CommandItem("id", "title", "", kind, "", Noop, Noop, ActionHint: first);
        Assert.Equal(new[] { first, second }, item.Actions.Select(a => a.Title));
        Assert.Equal($"Enter · {first}   Ctrl+Enter · {second}", item.KeyboardHint);
    }

    [Theory]
    [InlineData("w notepad", PaletteMode.Windows)]
    [InlineData("W ", PaletteMode.Windows)]
    [InlineData("=7", PaletteMode.Calculator)]
    [InlineData("?hello", PaletteMode.Web)]
    [InlineData("w", null)]
    [InlineData("word", null)]
    [InlineData(" w foo", null)]
    [InlineData("x=1", null)]
    public void PrefixOnlyAtStart(string text, PaletteMode? mode) => Assert.Equal(mode, PalettePrefixes.Match(text)?.Mode);

    [Fact]
    public async Task HelpEntersEnabledModesAndBackspaceExitsOnlyWhenEmpty()
    {
        PaletteMode entered = PaletteMode.Commands;
        var help = PalettePrefixes.Help(m => m != PaletteMode.Web, m => { entered = m; return Task.CompletedTask; });
        Assert.Equal(6, help.Count);
        await help[0].Item.Actions[0].Run();
        Assert.Equal(PaletteMode.Windows, entered);
        Assert.Equal(PaletteMode.Windows, PalettePrefixes.Backspace(entered, " "));
        Assert.Equal(PaletteMode.Commands, PalettePrefixes.Backspace(entered, ""));
    }

    [Fact]
    public void WindowExactMatchOutranksAppAndCannotBecomeRecent()
    {
        var usage = new PaletteUsageStore("unused.json");
        usage.Record("app", DateTimeOffset.UtcNow);
        usage.Record("window", DateTimeOffset.UtcNow);
        var app = new CommandItem("app", "Notepad", "", CommandKind.App, "", Noop);
        var window = new CommandItem("window", "Untitled – Notepad", "", CommandKind.Window, "", Noop, Synonyms: "notepad", RecordUsage: false);
        var search = new CommandSearch([new SnapshotCommandSource("apps", () => [app]), new SnapshotCommandSource("windows", () => [window], PaletteMode.Windows)], usage, (_, _) => { });
        Assert.Equal(new[] { "window", "app" }, search.Query("Notepad").Select(m => m.Item.Id));
        Assert.Equal("app", Assert.Single(search.Query("")).Item.Id);
        Assert.Equal("window", Assert.Single(search.Query("", mode: PaletteMode.Windows)).Item.Id);
    }

    [Theory]
    [InlineData("7 + 3 * (4 + 24)", "91")]
    [InlineData("2^3^2", "512")]
    [InlineData("-2^2", "-4")]
    [InlineData("2^-2", "0.25")]
    [InlineData("9%4", "1")]
    [InlineData("sqrt(81)+abs(-2)+round(1.8)", "13")]
    [InlineData("floor(2.9)+ceil(2.1)+max(1,4,3)+min(3,2)", "11")]
    [InlineData("sin(0)+cos(0)+tan(0)+log(100)+ln(e)", "4")]
    [InlineData("2*pi", "6.28318530718")]
    [InlineData("2/0", null)]
    [InlineData("1e308*2", null)]
    [InlineData("sqrt(-1)", null)]
    public void Math(string expression, string? expected) => Assert.Equal(expected, Calculator.Evaluate(expression, culture: Invariant)!.Result);

    [Fact]
    public void ExplicitMathAndCurrentCulture()
    {
        Assert.Null(Calculator.Evaluate("2024"));
        Assert.Null(Calculator.Evaluate("C++"));
        Assert.NotNull(Calculator.Evaluate("1+", true));
        Assert.Equal("2024", Calculator.Evaluate("2024", true, Invariant)?.Result);
        var culture = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("3,75", Calculator.Evaluate("1,5 + 2.25", culture: culture)?.Result);
        Assert.Equal("2,6", Calculator.Evaluate("max(1,5; 2,6)", culture: culture)?.Result);
    }

    [Theory]
    [InlineData("5 km to mi", "3.106")]
    [InlineData("5km in miles", "3.106")]
    [InlineData("2 kg to g", "2000")]
    [InlineData("100 f to c", "37.777")]
    [InlineData("2 cups to ml", "473.17")]
    [InlineData("2 ha to m2", "20000")]
    [InlineData("3 gb in mb", "3000")]
    [InlineData("1 KiB to B", "1024")]
    [InlineData("1 KB to B", "1000")]
    [InlineData("2 hr to s", "7200")]
    [InlineData("60 mph to kmh", "96.560")]
    public void UnitCategories(string text, string firstDigits) => Assert.StartsWith(firstDigits, Calculator.Evaluate(text, culture: Invariant)?.Result);

    [Fact]
    public void TimeZonesUseDstAndSupportTwelveAndTwentyFourHourInput()
    {
        var winter = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var summer = winter.AddMonths(6);
        string Eval(string text, DateTimeOffset date) => Calculator.Evaluate(text, culture: Invariant, now: date)?.Result ?? "";
        Assert.Contains("07:00", Eval("15:00 utc to pst", winter));
        Assert.Contains("08:00", Eval("15:00 utc to pst", summer));
        Assert.Contains("14:00", Eval("3pm london to utc", summer));
        Assert.Contains("21:00", Eval("time in tokyo", summer));
        Assert.Empty(Eval("time in atlantis", summer));
    }

    [Theory]
    [InlineData("github.com/x", "https://github.com/x")]
    [InlineData("https://example.com/", "https://example.com/")]
    [InlineData("data:text/html,hello", null)]
    [InlineData("https://a@b.com/", null)]
    [InlineData("not a url.com", null)]
    [InlineData("2024", null)]
    public void WebAddresses(string text, string? expected) => Assert.Equal(expected, WebSearch.Address(text)?.AbsoluteUri);

    [Fact]
    public async Task WebOnlyLaunchesOnActionAndEncodesQuery()
    {
        string? launched = null;
        const string template = "https://example.com/?q=%s";
        var source = new WebCommandSource(() => new(true, true, WebEngine.Custom, template), value => { launched = value; return Task.CompletedTask; });
        var row = Assert.Single(source.Query("a & b", PaletteMode.Web));
        Assert.Null(launched);
        await row.Item.Actions[0].Run();
        Assert.Equal("https://example.com/?q=a%20%26%20b", launched);
        Assert.False(WebSearch.IsValidTemplate("http://example.com?q=%s"));
        Assert.False(WebSearch.IsValidTemplate("https://%s.example.com"));
        Assert.False(WebSearch.IsValidTemplate("https://example.com"));
    }

    [Fact]
    public void ConfirmationIsScopedToActionExpiresAndCanBeCancelled()
    {
        TimeSpan At(int s) => TimeSpan.FromSeconds(s);
        var c = new PaletteConfirmation();
        Assert.False(c.Enter("restart", 0, At(1)));
        Assert.True(c.Enter("restart", 0, At(2)));
        Assert.False(c.Enter("restart", 0, At(3)));
        Assert.False(c.Enter("restart", 0, At(7)));
        Assert.False(c.Enter("shutdown", 0, At(8)));
        Assert.False(c.Enter("shutdown", 1, At(9)));
        c.Cancel();
        Assert.False(c.Enter("shutdown", 1, At(10)));
    }

    [Fact]
    public async Task SystemRowsExposeOnlyAvailableActionsAndNeverInvokeOnSearch()
    {
        var fake = new FakeSystem();
        var rows = SystemCommands.Create(fake, await fake.ReadAsync());
        Assert.DoesNotContain(rows, r => r.Id is "system:Hibernate" or "system:DndOn");
        Assert.Empty(rows.Single(r => r.Id == "system:EmptyRecycleBin").Actions);
        var restart = rows.Single(r => r.Id == "system:Restart");
        Assert.False(restart.RecordUsage);
        var action = Assert.Single(restart.Actions);
        Assert.True(action.NeedsConfirm);
        Assert.Null(fake.Ran);
        await action.Run();
        Assert.Equal(SystemCommand.Restart, fake.Ran);
    }

    private sealed class FakeSystem : IPaletteSystem
    {
        public SystemCommand? Ran;
        public Task<SystemPaletteState> ReadAsync() => Task.FromResult(new SystemPaletteState(false, 0, 0, true, false, null));
        public Task RunAsync(SystemCommand command) { Ran = command; return Task.CompletedTask; }
    }
}
