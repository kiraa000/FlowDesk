using System.Globalization;
using System.Text;

namespace FlowDesk.Core.Palette;

public static class CommandMatcher
{
    public sealed record Result(double Score, IReadOnlyList<MatchRange> Highlights);
    private sealed record Folded(string Text, int[] Starts, int[] Ends);
    private static Folded Fold(string text)
    {
        var result = new StringBuilder(); var starts = new List<int>(); var ends = new List<int>();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement(); int start = elements.ElementIndex;
            foreach (char c in element.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
                result.Append(char.ToUpperInvariant(c)); starts.Add(start); ends.Add(start + element.Length);
            }
        }
        return new(result.ToString(), starts.ToArray(), ends.ToArray());
    }
    public static Result Match(string text, string query)
    {
        var value = Fold(text); string q = Fold(query.Trim()).Text;
        if (q.Length == 0) return new(0, []);
        string t = value.Text; var indices = new List<int>(); double score;
        bool WordStart(int i) => i == 0 || !char.IsLetterOrDigit(t[i - 1]);
        int offset = t.IndexOf(q, StringComparison.Ordinal);
        if (offset == 0) { score = 500; indices.AddRange(Enumerable.Range(0, q.Length)); }
        else if (offset >= 0 && WordStart(offset)) { score = 400; indices.AddRange(Enumerable.Range(offset, q.Length)); }
        else
        {
            var words = Enumerable.Range(0, t.Length).Where(i => WordStart(i) && char.IsLetterOrDigit(t[i])).ToArray();
            string initials = new(words.Select(i => t[i]).ToArray());
            int acronym = initials.IndexOf(q, StringComparison.Ordinal);
            if (acronym >= 0) { score = 300; indices.AddRange(words.Skip(acronym).Take(q.Length)); }
            else if (offset >= 0) { score = 200; indices.AddRange(Enumerable.Range(offset, q.Length)); }
            else
            {
                int at = 0;
                foreach (char c in q) { int found = t.IndexOf(c, at); if (found < 0) return new(0, []); indices.Add(found); at = found + 1; }
                score = 100;
            }
        }
        // Small within-tier bonuses never overturn the five matching tiers.
        score += 20.0 / (1 + t.Length - q.Length) + 10.0 / (1 + indices[^1] - indices[0] + 1 - q.Length);
        var ranges = new List<MatchRange>();
        foreach (int i in indices)
        {
            int start = value.Starts[i], end = value.Ends[i];
            if (ranges.Count > 0 && start <= ranges[^1].Start + ranges[^1].Length)
            { var last = ranges[^1]; ranges[^1] = new(last.Start, Math.Max(end, last.Start + last.Length) - last.Start); }
            else ranges.Add(new(start, end - start));
        }
        return new(score, ranges);
    }
}
