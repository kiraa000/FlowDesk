using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteCustomCommandTests
{
    private static CustomCommandDefinition Url => new("yt", "YouTube", "yt youtube", CustomTargetKind.Url, "https://www.youtube.com/results?search_query={query}");
    [Fact] public async Task TypingOnlyBuildsRowsAndEnterRunsExactlyTheEncodedPlan()
    {
        var settings = new CustomCommandSettings { Entries = [Url] }; int runs = 0; CustomCommandLaunch? executed = null;
        var source = new CustomCommandSource(() => settings, plan => { runs++; executed = plan; return Task.CompletedTask; });
        var row = Assert.Single(source.Query("yt lofi & jazz"));
        Assert.Equal(0, runs); Assert.Contains("lofi%20%26%20jazz", row.Item.Subtitle);
        await row.Item.Actions[0].Run(); Assert.Equal(1, runs); Assert.Equal("https://www.youtube.com/results?search_query=lofi%20%26%20jazz", executed?.Target);
    }
    [Theory] [InlineData("w")] [InlineData("f")] [InlineData("b")] [InlineData("AI")] [InlineData("help")] [InlineData("?")] [InlineData("bad/name")]
    public void BuiltInAndInvalidKeywordsAreRejected(string word) => Assert.NotNull(CustomCommandRules.Validate(Url with { Keywords = word }, []));
    [Fact] public void KeywordsAreUniqueIgnoringCaseButEditingSameIdentityIsAllowed()
    {
        Assert.Null(CustomCommandRules.Validate(Url, [Url]));
        Assert.NotNull(CustomCommandRules.Validate(Url with { Id = "other", Keywords = "YT" }, [Url]));
        Assert.NotNull(CustomCommandRules.Validate(Url with { Keywords = "yt YT" }, []));
    }
    [Fact] public void NameUrlPathAndExistenceValidationAreInlineFriendly()
    {
        Assert.NotNull(CustomCommandRules.Validate(Url with { Name = " " }, []));
        Assert.NotNull(CustomCommandRules.Validate(Url with { Target = "javascript:alert(1)" }, []));
        Assert.NotNull(CustomCommandRules.Validate(Url with { Kind = CustomTargetKind.File, Target = "relative.txt" }, []));
        Assert.NotNull(CustomCommandRules.Validate(Url with { Kind = CustomTargetKind.File, Target = @"D:\missing.txt" }, [], _ => false));
        Assert.NotNull(CustomCommandRules.Validate(Url with { Kind = CustomTargetKind.PowerShellScript, Target = @"D:\script.cmd" }, []));
        Assert.NotNull(CustomCommandRules.Validate(Url with { RunAsAdmin = true }, []));
    }
    [Fact] public void ScriptsAreVisibleNoProfileFileInvocationsWithoutPolicyOverride()
    {
        var script = Url with { Kind = CustomTargetKind.PowerShellScript, Target = @"D:\my scripts\test.ps1", Arguments = "-Name \"{query}\"" };
        var plan = CustomCommandRules.Build(script, "sample");
        Assert.Equal("powershell.exe", plan.Target); Assert.Equal("-NoProfile -File \"D:\\my scripts\\test.ps1\" -Name \"sample\"", plan.Arguments);
        Assert.False(plan.RunAsAdmin); Assert.DoesNotContain("ExecutionPolicy", plan.Arguments);
        Assert.True(CustomCommandRules.Build(script with { RunAsAdmin = true }, "").RunAsAdmin);
    }
    [Fact] public void ProgramArgumentsAreSubstitutedWithoutUrlEncoding()
    {
        var program = Url with { Kind = CustomTargetKind.Program, Target = @"D:\tools\app.exe", Arguments = "--find \"{query}\"" };
        Assert.Equal("--find \"two words\"", CustomCommandRules.Build(program, "two words").Arguments);
    }
    [Fact] public void UnknownVersionIsInertAndInvalidEntriesCannotLaunch()
    {
        var settings = new CustomCommandSettings { Version = 2, Entries = [Url] };
        var source = new CustomCommandSource(() => settings, _ => throw new Exception("Must not run"));
        Assert.Empty(source.Query("yt test"));
        settings.Version = 1; settings.Entries = [Url with { Target = "invalid" }]; Assert.Empty(source.Query("yt test"));
    }
    [Theory] [InlineData("word", "\"word\"")] [InlineData("a\"b", "\"a\\\"b\"")] [InlineData("D:\\folder\\", "\"D:\\folder\\\\\"")]
    public void WindowsArgumentQuotePreservesQuotesAndTrailingBackslashes(string input, string expected) => Assert.Equal(expected, CustomCommandRules.Quote(input));
}
