using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteFileMatchingTests
{
    [Theory]
    [InlineData("Forza Horizon 6 (v440.853 + 23 DLCs + Multiplayer MULTi23) [FitGirl Repack Selective Download - from 81.6 GB].zip")]
    [InlineData("FlowLauncher-Backup-2026-09-08_22-20-12-Settings")]
    [InlineData("Files Other Copies Under Storage")]
    public void FilesRejectScatteredLettersAndAcronyms(string title)
    {
        var item = new CommandItem("file", title, "File", CommandKind.File, "", () => Task.CompletedTask, Synonyms: @"D:\Backups\" + title);
        var source = new SnapshotCommandSource("files", () => [item]);
        Assert.Empty(source.Query("focus"));
        Assert.Equal(0, CommandMatcher.Match(title, "focus", allowFuzzy: false).Score);
    }
    [Theory]
    [InlineData("Focus notes.txt")]
    [InlineData("My focus notes.txt")]
    [InlineData("Refocusing.txt")]
    [InlineData("Fócus notes.txt")]
    public void FilesStillMatchContiguousNamesWithCaseAndDiacritics(string title)
    {
        var result = CommandMatcher.Match(title, "FOCUS", allowFuzzy: false);
        Assert.True(result.Score > 0); Assert.Single(result.Highlights);
    }
    [Fact] public void FilePathsAreSearchableWithoutFuzzyAncestorMatches()
    {
        var item = new CommandItem("file", "notes.txt", "File", CommandKind.File, "", () => Task.CompletedTask, Synonyms: @"D:\Study\Focus\notes.txt");
        var source = new SnapshotCommandSource("files", () => [item]);
        Assert.Single(source.Query(@"Study\Focus"));
        Assert.Empty(source.Query("sfnts"));
        Assert.Single(source.Query("")); // empty query MRU eligibility stays independent of matching mode
    }
    [Fact] public void UsageBoostDoesNotResurrectJunkFileMatches()
    {
        var item = new CommandItem("file", "Files Other Copies Under Storage", "File", CommandKind.File, "", () => Task.CompletedTask);
        var usage = new PaletteUsageStore(Path.Combine(Path.GetTempPath(), "unused-palette-r2-usage.json"));
        for (int i = 0; i < 100; i++) usage.Record(item.Id, DateTimeOffset.UtcNow);
        var search = new CommandSearch([new SnapshotCommandSource("files", () => [item])], usage, (_, _) => { });
        Assert.Empty(search.Query("focus"));
    }
    [Fact] public void AppsKeepAcronymsAndCompactSubsequencesButNotLongOrWidelyScatteredOnes()
    {
        Assert.True(CommandMatcher.Match("Visual Studio Code", "vsc").Score >= 300);
        Assert.True(CommandMatcher.Match("FlowDesk", "fld").Score >= 100);
        Assert.Equal(0, CommandMatcher.Match("F" + new string('x', 70) + "ocus", "focus").Score);
        Assert.Equal(0, CommandMatcher.Match("FxxxxxxxxOxxxxxxxxCxxxxxxxxUxxxxxxxxS", "focus").Score);
    }
}
