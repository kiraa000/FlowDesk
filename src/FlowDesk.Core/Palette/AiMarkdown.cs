namespace FlowDesk.Core.Palette;

public sealed record AiTextSpan(string Text, bool Code = false, bool Bold = false, bool Italic = false);
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
            if (!code && Heading(line) is { } heading)
            { Flush(); result.Add(new(Inline(heading).Select(s => s with { Bold = true }).ToArray())); }
            else if (!code && ListItem(line) is { } list)
            { Flush(); paragraph.Add(list); Flush(); }
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
    private static string? Heading(string line)
    {
        string text = line.TrimStart(); int hashes = 0;
        while (hashes < text.Length && text[hashes] == '#') hashes++;
        return hashes is >= 1 and <= 3 && hashes < text.Length && char.IsWhiteSpace(text[hashes]) ? text[hashes..].Trim() : null;
    }
    private static string? ListItem(string line)
    {
        string text = line.TrimStart();
        if (text.StartsWith("- ") || text.StartsWith("* ")) return "• " + text[2..];
        int digits = 0;
        while (digits < Math.Min(9, text.Length) && char.IsAsciiDigit(text[digits])) digits++;
        return digits > 0 && digits + 1 < text.Length && text[digits] is '.' or ')' && char.IsWhiteSpace(text[digits + 1])
            ? text[..digits] + ". " + text[(digits + 1)..].TrimStart() : null;
    }
    private static IReadOnlyList<AiTextSpan> Inline(string text) => new InlineParser(text).Parse(0, text.Length);
    // Deliberately small text-only subset: no HTML, links or arbitrary XAML. A shared scan budget
    // and depth/run limits keep incomplete/adversarial streamed delimiters from quadratic work.
    private sealed class InlineParser(string text)
    {
        private int budget = text.Length * 4;
        public IReadOnlyList<AiTextSpan> Parse(int start, int end, bool bold = false, bool italic = false, int depth = 0)
        {
            var spans = new List<AiTextSpan>(); var plain = new System.Text.StringBuilder();
            for (int at = start; at < end;)
            {
                if (budget <= 0 || spans.Count >= 2048) { plain.Append(text[at..end]); break; }
                char current = text[at];
                if (current == '\\' && at + 1 < end && text[at + 1] is '*' or '_' or '`' or '\\')
                { plain.Append(text[at + 1]); at += 2; continue; }
                string marker = current == '`' ? "`" : current is '*' or '_' ? new string(current, at + 1 < end && text[at + 1] == current ? 2 : 1) : "";
                bool underscoreInWord = current == '_' && at > start && Word(text[at - 1]);
                bool singleInWord = marker.Length == 1 && current != '`' && at > start && Word(text[at - 1]) && at + 1 < end && Word(text[at + 1]);
                if (marker.Length > 0 && at + marker.Length < end && !char.IsWhiteSpace(text[at + marker.Length]) &&
                    !underscoreInWord && !singleInWord && depth < 4 && Closing(marker, at + marker.Length, end) is int close && close >= 0)
                {
                    Flush();
                    if (current == '`') spans.Add(new(text[(at + 1)..close], true, bold, italic));
                    else spans.AddRange(Parse(at + marker.Length, close, bold || marker.Length == 2, italic || marker.Length == 1, depth + 1));
                    at = close + marker.Length;
                }
                else { plain.Append(current); at++; }
            }
            Flush(); return spans;
            void Flush() { if (plain.Length > 0) { spans.Add(new(plain.ToString(), Bold: bold, Italic: italic)); plain.Clear(); } }
        }
        private int Closing(string marker, int start, int end)
        {
            int cursor = start;
            while (cursor < end && budget > 0)
            {
                int close = text.IndexOf(marker, cursor, end - cursor, StringComparison.Ordinal);
                budget -= close < 0 ? end - cursor : close - cursor + marker.Length;
                if (close < 0 || budget < 0) return -1;
                if (marker.Length == 2)
                    while (close + 2 < end && text[close + 2] == marker[0]) close++;
                bool escaped = close > 0 && text[close - 1] == '\\';
                bool touchesWord = marker[0] == '_' && close + marker.Length < end && Word(text[close + marker.Length]);
                bool partOfDouble = marker.Length == 1 && marker[0] != '`' &&
                    ((close > start && text[close - 1] == marker[0]) || (close + 1 < end && text[close + 1] == marker[0]));
                if (!escaped && !touchesWord && !partOfDouble && close > start && !char.IsWhiteSpace(text[close - 1])) return close;
                cursor = close + marker.Length;
            }
            return -1;
        }
        private static bool Word(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
