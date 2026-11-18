using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteRunTests
{
    [Theory]
    [InlineData("cmd /k echo hello", "cmd", "/k echo hello")]
    [InlineData("wt", "wt", "")]
    [InlineData("notepad file.txt", "notepad", "file.txt")]
    [InlineData("shell:startup", "shell:startup", "")]
    [InlineData("\"D:\\Program Files\\app.exe\" \"my file.txt\"", "D:\\Program Files\\app.exe", "\"my file.txt\"")]
    [InlineData("notepad\tfile.txt", "notepad", "file.txt")]
    public void ParsesOnlyTheFirstTokenAndPreservesRemainingArgumentText(string text, string target, string arguments)
    {
        var line = RunLineParser.Parse(text)!; Assert.Equal(target, line.Target); Assert.Equal(arguments, line.Arguments);
    }
    [Theory] [InlineData("")] [InlineData("   ")] [InlineData("\"unterminated file")] [InlineData("notepad\0x")] [InlineData("cmd\necho no")]
    public void EmptyMalformedOrMultilineCommandsAreInert(string text) => Assert.Null(RunLineParser.Parse(text));
    [Fact] public void ExpandsEnvironmentBeforeTokenParsingAndKeepsOriginalForHistory()
    {
        var parsed = RunLineParser.Parse("\"%TOOLS%\\app.exe\" %HOME%", s => s.Replace("%TOOLS%", "D:\\my tools").Replace("%HOME%", "D:\\home"))!;
        Assert.Equal("D:\\my tools\\app.exe", parsed.Target); Assert.Equal("D:\\home", parsed.Arguments); Assert.Contains("%TOOLS%", parsed.Original);
        Assert.True(parsed.Launch(true).RunAsAdmin); Assert.False(parsed.Launch().RunAsAdmin);
        Assert.Equal("cmd.exe", parsed.Terminal().Target); Assert.StartsWith("/k ", parsed.Terminal().Arguments);
    }
    [Fact] public async Task TypingNeverLaunchesAndHistoryAppearsOnlyAfterExecution()
    {
        var usage = new PaletteUsageStore("not-written.json"); int calls = 0, saves = 0;
        var source = new RunCommandSource(usage, _ => { calls++; return Task.CompletedTask; }, () => { saves++; return Task.CompletedTask; }, () => { });
        Assert.Empty(source.Query("notepad", PaletteMode.Commands));
        var row = Assert.Single(source.Query("notepad file.txt")); Assert.Equal(0, calls); Assert.Empty(usage.RunHistory());
        Assert.Equal("Run as administrator", row.Item.Actions[1].Title);
        await row.Item.Actions[0].Run(); Assert.Equal(1, calls); Assert.Equal(1, saves);
        var history = Assert.Single(source.Query("")); Assert.Equal("notepad file.txt", history.Item.Title);
        await history.Item.Actions.Single(a => a.Title == "Delete from history").Run(); Assert.Empty(usage.RunHistory());
    }
    [Fact] public async Task FailedLaunchDoesNotRecordHistory()
    {
        var usage = new PaletteUsageStore("not-written.json");
        var source = new RunCommandSource(usage, _ => throw new IOException("Not installed"), () => Task.CompletedTask, () => { });
        await Assert.ThrowsAsync<IOException>(() => Assert.Single(source.Query("missing.exe")).Item.Actions[0].Run()); Assert.Empty(usage.RunHistory());
    }
    [Fact] public async Task HistoryIsCappedDeduplicatedPersistedAndIndependentOfOtherUsageCapacity()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "run-history-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "usage.json");
        var store = new PaletteUsageStore(path, 1); var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 25; i++) store.RecordRun("notepad " + i, now.AddSeconds(i));
        store.RecordRun("notepad 10", now.AddSeconds(30)); Assert.Equal(20, store.RunHistory().Count); Assert.Equal("notepad 10", store.RunHistory()[0]);
        store.Record("app:one", now.AddSeconds(40)); store.Record("app:two", now.AddSeconds(41));
        Assert.Equal(20, store.RunHistory().Count); await store.SaveAsync();
        var loaded = new PaletteUsageStore(path, 1); loaded.Load(); Assert.Equal(store.RunHistory(), loaded.RunHistory());
        loaded.ClearRunHistory(); Assert.Empty(loaded.RunHistory()); Assert.Equal(1, loaded.Get("app:two").Count);
        // Test artifact retained under the project's test output, never the installed app's data.
    }
    [Fact] public void RunPrefixIsListedWithoutChangingOtherPrefixMatches()
    { Assert.Equal(PaletteMode.Run, PalettePrefixes.Match(">notepad")?.Mode); Assert.Equal(PaletteMode.Files, PalettePrefixes.Match("f file")?.Mode); }
}
