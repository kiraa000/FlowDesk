using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteBookmarkTests
{
    private static BookmarkProfile Chrome => new(BookmarkBrowser.Chrome, "Work", @"D:\Chrome\Profile 1", "chrome.exe");
    [Fact] public void NestedChromiumFoldersAndAllRootsAreParsedAndUnsafeUrlsIgnored()
    {
        const string json = """
        {"roots":{"bookmark_bar":{"type":"folder","children":[{"type":"folder","children":[
          {"type":"url","id":"1","name":"Docs","url":"https://example.com/docs"},
          {"type":"url","id":"2","name":"Unsafe","url":"javascript:alert(1)"}]}]},
          "other":{"type":"folder","children":[{"type":"url","id":"3","name":"Mail","url":"https://mail.example.com"}]}}}
        """;
        var entries = BookmarkParser.Chromium(json, Chrome);
        Assert.Equal(2, entries.Count); Assert.Contains(entries, e => e.Title == "Docs"); Assert.All(entries, e => Assert.Contains("Work", e.Subtitle));
        var personal = BookmarkParser.Chromium(json, Chrome with { Path = @"D:\Chrome\Default", Name = "Personal" });
        Assert.NotEqual(entries[0].Id, personal[0].Id);
    }
    [Fact] public void ChromiumAndFirefoxLaunchArgumentsReuseProfileSelectorsWithoutOtherFlags()
    {
        Assert.Contains("--profile-directory=\"Profile 1\"", Chrome.Arguments); Assert.Contains("--user-data-dir=D:\\Chrome", Chrome.Arguments);
        var firefox = new BookmarkProfile(BookmarkBrowser.Firefox, "Work", @"D:\Firefox Profiles\Work", "firefox.exe");
        Assert.Equal("-profile \"D:\\Firefox Profiles\\Work\"", firefox.Arguments);
    }
    [Fact] public void FirefoxIniIncludesRelativeAndAbsoluteProfilesButNotInstallSections()
    {
        const string ini = "[Profile0]\nName=Personal\nIsRelative=1\nPath=Profiles/personal\n[Install123]\nDefault=ignored\n[Profile1]\nName=Work\nIsRelative=0\nPath=D:\\Firefox Work\n";
        var profiles = BookmarkParser.FirefoxProfiles(ini, @"D:\Mozilla\Firefox", "firefox.exe");
        Assert.Equal(2, profiles.Count); Assert.Equal(@"D:\Mozilla\Firefox\Profiles\personal", profiles[0].Path); Assert.Equal(@"D:\Firefox Work", profiles[1].Path);
    }
    [Fact] public void PrefixAndBrowserOptionsHaveExpectedDefaults()
    {
        Assert.Equal(PaletteMode.Bookmarks, PalettePrefixes.Match("b docs")?.Mode);
        var all = new BookmarkOptions(); Assert.All(Enum.GetValues<BookmarkBrowser>(), b => Assert.True(all.Enabled(b)));
        Assert.False((all with { Chrome = false }).Enabled(BookmarkBrowser.Chrome));
    }
    [Fact] public void MixedBookmarkRankingCannotPushAppsOrWindowsBelowBookmarks()
    {
        CommandMatch Row(string id, CommandKind kind, double score) => new(new(id, id, "", kind, "", () => Task.CompletedTask), score, []);
        var merged = PaletteResultMerge.Merge([Row("bookmark", CommandKind.Bookmark, 10000), Row("window", CommandKind.Window, 1), Row("app", CommandKind.App, 2)], PaletteMode.Commands);
        Assert.Equal("bookmark", merged[^1].Item.Id);
    }
}
