using System.Globalization;

namespace FlowDesk.Core.Palette;

public sealed record Calculation(string? Result);

public static class Calculator
{
    public static Calculation? Evaluate(string input, bool explicitMode = false, CultureInfo? culture = null, DateTimeOffset? now = null)
    {
        input = input.Trim();
        if (input.Length == 0 || input.Length > 512) return null;
        culture ??= CultureInfo.CurrentCulture;
        var conversion = UnitConversions.Evaluate(input, culture) ?? CityTimeZones.Evaluate(input, culture, now ?? DateTimeOffset.Now);
        if (conversion is not null) return new(conversion);
        var parser = new Parser(input, culture.NumberFormat.NumberDecimalSeparator);
        try
        {
            double value = parser.Parse();
            if (!explicitMode && !parser.HasOperation) return null;
            return new(parser.Invalid ? null : (value == 0 ? 0 : value).ToString("G12", culture));
        }
        catch (FormatException) { return explicitMode ? new(null) : null; }
    }

    private sealed class Parser(string text, string separator)
    {
        private int pos;
        public bool HasOperation { get; private set; }
        public bool Invalid { get; private set; }

        public double Parse()
        {
            double result = Expression(0);
            Skip();
            if (pos != text.Length) throw new FormatException();
            return result;
        }

        // Binding powers: terms, products, prefix sign, right-associative power.
        private double Expression(int minimum)
        {
            Skip();
            double left;
            if (Take('-')) left = -Expression(30);
            else if (Take('+')) left = Expression(30);
            else left = Atom();
            while (true)
            {
                Skip();
                char op = pos < text.Length ? text[pos] : '\0';
                int precedence = op switch { '+' or '-' => 10, '*' or '/' or '%' => 20, '^' => 40, _ => -1 };
                if (precedence < minimum) return Finite(left);
                pos++;
                HasOperation = true;
                double right = Expression(precedence + (op == '^' ? 0 : 1));
                left = Finite(op switch { '+' => left + right, '-' => left - right, '*' => left * right,
                    '/' => left / right, '%' => left % right, _ => Math.Pow(left, right) });
            }
        }

        private double Atom()
        {
            if (Take('(')) { double grouped = Expression(0); Require(')'); return grouped; }
            int start = pos;
            while (pos < text.Length && char.IsAsciiLetter(text[pos])) pos++;
            if (pos == start) return Number();
            string name = text[start..pos].ToLowerInvariant();
            if (name == "pi") return Math.PI;
            if (name == "e") return Math.E;
            Require('(');
            double value = Expression(0);
            if (name is "min" or "max")
            {
                if (!Take(';')) Require(',');
                do { double next = Expression(0); value = name == "min" ? Math.Min(value, next) : Math.Max(value, next); }
                while (Take(';') || Take(','));
            }
            else value = name switch
            {
                "sqrt" => Math.Sqrt(value), "abs" => Math.Abs(value), "round" => Math.Round(value),
                "floor" => Math.Floor(value), "ceil" => Math.Ceiling(value), "sin" => Math.Sin(value),
                "cos" => Math.Cos(value), "tan" => Math.Tan(value), "log" => Math.Log10(value),
                "ln" => Math.Log(value), _ => throw new FormatException()
            };
            Require(')'); HasOperation = true;
            return Finite(value);
        }

        private double Number()
        {
            int start = pos;
            Digits();
            if (pos < text.Length && text[pos] == '.') { pos++; Digits(); }
            else if (separator != "." && text.AsSpan(pos).StartsWith(separator, StringComparison.Ordinal) &&
                     pos + separator.Length < text.Length && char.IsAsciiDigit(text[pos + separator.Length]))
            { pos += separator.Length; Digits(); }
            if (pos < text.Length && text[pos] is 'E' or 'e')
            {
                pos++;
                if (pos < text.Length && text[pos] is '+' or '-') pos++;
                int exponent = pos; Digits();
                if (pos == exponent) throw new FormatException();
            }
            string number = text[start..pos].Replace(separator, ".", StringComparison.Ordinal);
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) throw new FormatException();
            return Finite(value);
        }

        private double Finite(double value) { Invalid |= !double.IsFinite(value); return value; }
        private void Digits() { while (pos < text.Length && char.IsAsciiDigit(text[pos])) pos++; }
        private void Skip() { while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++; }
        private bool Take(char ch) { Skip(); if (pos == text.Length || text[pos] != ch) return false; pos++; return true; }
        private void Require(char ch) { if (!Take(ch)) throw new FormatException(); }
    }
}

public sealed class CalculatorCommandSource(Func<bool> enabled, Func<string, Task> copy) : IModeCommandSource
{
    public string Name => "Calculator";
    public PaletteMode Mode => PaletteMode.Calculator;
    public IEnumerable<CommandMatch> Query(string query) => Query(query, PaletteMode.Commands);
    public IEnumerable<CommandMatch> Query(string query, PaletteMode mode)
    {
        if (!enabled() || Calculator.Evaluate(query, mode == Mode) is not { } calculation) yield break;
        string? result = calculation.Result;
        Func<Task> primary = result is null ? () => Task.CompletedTask : () => copy(result);
        yield return new(new("calculator", result is null ? "Cannot calculate" : "= " + result, query.Trim(), CommandKind.Calculator,
            "\uE8EF", primary, RecordUsage: false, NamedActions: result is null ? [] :
            [new("Copy result", primary, "\uE8C8", "Enter"), new("Copy expression and result", () => copy($"{query.Trim()} = {result}"), "\uE8C8", "Ctrl+Enter")]), 20_000, []);
    }
}
