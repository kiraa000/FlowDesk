namespace FlowDesk.Core.Palette;

public sealed record AiTextSpan(string Text, bool Code = false);
public sealed record AiTextBlock(IReadOnlyList<AiTextSpan> Spans, bool Code = false);
public static class AiMarkdown
{
    public static IReadOnlyList<AiTextBlock> Parse(string text)
    {
        var result = new List<AiTextBlock>(); var paragraph = new List<string>(); bool code = false;
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { Flush(); code = !code; continue; }
            if (!code && line.Length == 0) { Flush(); continue; }
            if (!code && (line.StartsWith("- ") || line.StartsWith("* "))) { Flush(); paragraph.Add("• " + line[2..]); Flush(); }
            else paragraph.Add(line);
        }
        Flush(); return result;
        void Flush()
        {
            if (paragraph.Count == 0) return;
            string content = string.Join('\n', paragraph); paragraph.Clear();
            result.Add(new(code ? [new(content, true)] : Inline(content), code));
        }
    }
    private static IReadOnlyList<AiTextSpan> Inline(string text)
    {
        var spans = new List<AiTextSpan>(); int start = 0;
        while (start < text.Length)
        {
            int open = text.IndexOf('`', start);
            int close = open >= 0 ? text.IndexOf('`', open + 1) : -1;
            if (close < 0) { spans.Add(new(text[start..])); break; }
            if (open > start) spans.Add(new(text[start..open]));
            spans.Add(new(text[(open + 1)..close], true)); start = close + 1;
        }
        return spans;
    }
}
