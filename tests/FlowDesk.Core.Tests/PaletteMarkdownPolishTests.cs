using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteMarkdownPolishTests
{
    private static string Plain(IReadOnlyList<AiTextSpan> spans) => string.Concat(spans.Select(s => s.Text));
    [Theory] [InlineData("**bold**")] [InlineData("__bold__")]
    public void StrongMarkersAreRemovedAndAppliedToText(string text)
    {
        var span = Assert.Single(Assert.Single(AiMarkdown.Parse(text)).Spans);
        Assert.Equal("bold", span.Text); Assert.True(span.Bold); Assert.False(span.Italic);
    }
    [Theory] [InlineData("*italic*")] [InlineData("_italic_")]
    public void UnambiguousEmphasisIsItalic(string text)
    {
        var span = Assert.Single(Assert.Single(AiMarkdown.Parse(text)).Spans);
        Assert.Equal("italic", span.Text); Assert.True(span.Italic); Assert.False(span.Bold);
    }
    [Fact] public void NestedStylesAndCodeRemainSeparate()
    {
        var spans = Assert.Single(AiMarkdown.Parse("**bold *italic* `literal_*`**")).Spans;
        Assert.Equal("bold italic literal_*", Plain(spans)); Assert.All(spans, s => Assert.True(s.Bold));
        Assert.Contains(spans, s => s.Text == "italic" && s.Italic);
        Assert.Contains(spans, s => s.Code && s.Text == "literal_*");
        Assert.Equal("**literal** _code_", Assert.Single(Assert.Single(AiMarkdown.Parse("`**literal** _code_`")).Spans).Text);
    }
    [Fact] public void HeadingsUseBoldLinesButHashesWithoutWhitespaceAndLevelFourStayLiteral()
    {
        var blocks = AiMarkdown.Parse("# One\n## Two\n### Three\n#### Four\n#tag");
        Assert.Equal(4, blocks.Count);
        Assert.Equal(new[] { "One", "Two", "Three" }, blocks.Take(3).Select(b => Plain(b.Spans)));
        Assert.All(blocks.Take(3).SelectMany(b => b.Spans), s => Assert.True(s.Bold));
        Assert.Equal("#### Four\n#tag", Plain(blocks[3].Spans));
    }
    [Fact] public void NumberedAndBulletItemsGetSeparateLinesAndFencesStayLiteral()
    {
        var blocks = AiMarkdown.Parse("1. **First**\n2) Second\n- Third\n```\n# literal\n*literal*\n```");
        Assert.Equal(4, blocks.Count); Assert.Equal("1. First", Plain(blocks[0].Spans));
        Assert.Equal("2. Second", Plain(blocks[1].Spans)); Assert.Equal("• Third", Plain(blocks[2].Spans));
        Assert.True(blocks[3].Code); Assert.Equal("# literal\n*literal*", Plain(blocks[3].Spans));
    }
    [Theory] [InlineData("snake_case_name")] [InlineData("x_y_z")] [InlineData("2*3*4")]
    [InlineData("file__name__part")] [InlineData("**incomplete")] [InlineData("_ with spaces _")]
    public void AmbiguousAndIncompleteMarkersRemainLiteral(string text)
    { var spans = Assert.Single(AiMarkdown.Parse(text)).Spans; Assert.Equal(text, Plain(spans)); Assert.All(spans, s => Assert.False(s.Bold || s.Italic)); }
    [Fact] public void EscapesHtmlAndLinksAreOnlyInertTextAndMalformedInputIsBounded()
    {
        Assert.Equal("*literal*", Plain(Assert.Single(AiMarkdown.Parse(@"\*literal\*")).Spans));
        const string text = "<script>no HTML</script> [link](https://example.com)";
        Assert.Equal(text, Plain(Assert.Single(AiMarkdown.Parse(text)).Spans));
        string malformed = string.Concat(Enumerable.Repeat("*a ", 5000));
        Assert.Equal(malformed, Plain(Assert.Single(AiMarkdown.Parse(malformed)).Spans));
    }
}
