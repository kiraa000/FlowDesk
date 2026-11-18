using System.Globalization;

namespace FlowDesk.Core.Palette;

/// <summary>
/// Pure unit conversion for palette queries ("5 km to mi", "100 f to c", "3 gb in mb").
/// Returns "value symbol" formatted with the supplied culture (no group separators, up to ten
/// significant digits), or null when the text is not a same-dimension conversion.
/// Data units are decimal (KB = 1000 bytes) unless the binary spelling is used (KiB = 1024 bytes);
/// volume uses US customary units. Temperature conversions include their offsets.
/// </summary>
internal static class UnitConversions
{
    private enum Dimension { Length, Mass, Temperature, Volume, Area, Data, Time, Speed }
    private enum TemperatureScale { Celsius, Fahrenheit, Kelvin }
    private sealed record UnitDefinition(Dimension Dimension, double Factor, string Symbol, TemperatureScale? Temperature = null);

    private static readonly Dictionary<string, UnitDefinition> Units = BuildUnits();

    public static string? Evaluate(string input, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();

        // "5 in to cm" must split on " to " first; "5 in in cm" splits on the last " in ".
        int split = text.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
        if (split < 0) split = text.LastIndexOf(" in ", StringComparison.OrdinalIgnoreCase);
        if (split <= 0) return null;

        if (!TryParseValueAndUnit(text[..split], culture, out var value, out var from)) return null;
        if (!TryResolveUnit(text[(split + 4)..], out var to)) return null;
        if (from.Dimension != to.Dimension) return null;

        double result;
        if (from.Dimension == Dimension.Temperature)
        {
            if (from.Temperature is not { } fromScale || to.Temperature is not { } toScale) return null;
            double kelvin = fromScale switch
            {
                TemperatureScale.Celsius => value + 273.15,
                TemperatureScale.Fahrenheit => (value - 32) * 5 / 9 + 273.15,
                TemperatureScale.Kelvin => value,
                _ => double.NaN
            };
            result = toScale switch
            {
                TemperatureScale.Celsius => kelvin - 273.15,
                TemperatureScale.Fahrenheit => (kelvin - 273.15) * 9 / 5 + 32,
                TemperatureScale.Kelvin => kelvin,
                _ => double.NaN
            };
        }
        else
        {
            result = value * from.Factor / to.Factor;
        }

        return double.IsFinite(result) ? $"{Format(result, culture)} {to.Symbol}" : null;
    }

    private static bool TryParseValueAndUnit(string text, CultureInfo culture, out double value, out UnitDefinition unit)
    {
        value = 0;
        unit = null!;
        text = text.Trim();
        int index = 0;
        if (index < text.Length && text[index] is '+' or '-') index++;
        int digits = index;
        string separator = culture.NumberFormat.NumberDecimalSeparator;
        bool decimalSeen = false;
        while (index < text.Length)
        {
            if (char.IsAsciiDigit(text[index])) { index++; continue; }
            if (decimalSeen) break;
            if (text[index] == '.') index++;
            else if (text.AsSpan(index).StartsWith(separator, StringComparison.Ordinal)) index += separator.Length;
            else break;
            decimalSeen = true;
        }
        if (index == digits) return false;
        var numberText = text[..index];
        var unitText = text[index..].Trim();
        if (unitText.Length == 0) return false;
        if (!double.TryParse(numberText.Replace(separator, ".", StringComparison.Ordinal), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) || !double.IsFinite(value)) return false;
        return TryResolveUnit(unitText, out unit);
    }

    private static bool TryResolveUnit(string text, out UnitDefinition unit)
    {
        var key = NormalizeUnit(text);
        if (Units.TryGetValue(key, out unit!)) return true;
        if (key.Length > 1 && key[^1] == 's' && Units.TryGetValue(key[..^1], out unit!)) return true;
        return false;
    }

    private static string NormalizeUnit(string text)
    {
        var buffer = new char[text.Length];
        int length = 0;
        foreach (var raw in text)
        {
            char c = char.ToLowerInvariant(raw);
            if (c is ' ' or '\t' or '\r' or '\n' or '\u00A0' or '.' or '*' or '°') continue;
            if (c == '²') c = '2';
            else if (c == '³') c = '3';
            buffer[length++] = c;
        }
        return new string(buffer, 0, length);
    }

    private static string Format(double value, CultureInfo culture)
    {
        if (value == 0) return "0";
        double magnitude = Math.Abs(value);
        if (magnitude >= 1e12 || magnitude < 1e-9) return value.ToString("0.######E+0", culture);
        int decimals = Math.Clamp(9 - (int)Math.Floor(Math.Log10(magnitude)), 0, 12);
        return Math.Round(value, decimals).ToString("0.############", culture);
    }

    private static Dictionary<string, UnitDefinition> BuildUnits()
    {
        var units = new Dictionary<string, UnitDefinition>(StringComparer.Ordinal);

        void Add(Dimension dimension, double factor, string symbol, params string[] aliases)
        {
            var definition = new UnitDefinition(dimension, factor, symbol);
            foreach (var alias in aliases) units[alias] = definition;
        }

        void AddTemperature(TemperatureScale scale, string symbol, params string[] aliases)
        {
            var definition = new UnitDefinition(Dimension.Temperature, 0, symbol, scale);
            foreach (var alias in aliases) units[alias] = definition;
        }

        Add(Dimension.Length, 1, "m", "m", "meter", "metre");
        Add(Dimension.Length, 1000, "km", "km", "kilometer", "kilometre");
        Add(Dimension.Length, 0.01, "cm", "cm", "centimeter", "centimetre");
        Add(Dimension.Length, 0.001, "mm", "mm", "millimeter", "millimetre");
        Add(Dimension.Length, 1e-6, "µm", "um", "micrometer", "micrometre", "micron");
        Add(Dimension.Length, 1609.344, "mi", "mi", "mile");
        Add(Dimension.Length, 0.3048, "ft", "ft", "foot", "feet");
        Add(Dimension.Length, 0.0254, "in", "in", "inch", "inches");
        Add(Dimension.Length, 0.9144, "yd", "yd", "yard");
        Add(Dimension.Length, 1852, "nmi", "nmi", "nauticalmile");

        Add(Dimension.Mass, 1, "kg", "kg", "kilogram", "kilo");
        Add(Dimension.Mass, 0.001, "g", "g", "gram");
        Add(Dimension.Mass, 1e-6, "mg", "mg", "milligram");
        Add(Dimension.Mass, 0.45359237, "lb", "lb", "pound");
        Add(Dimension.Mass, 0.028349523125, "oz", "oz", "ounce");
        Add(Dimension.Mass, 6.35029318, "st", "st", "stone");
        Add(Dimension.Mass, 1000, "t", "t", "tonne", "ton", "metricton");

        AddTemperature(TemperatureScale.Celsius, "°C", "c", "celsius", "degc", "degreecelsius");
        AddTemperature(TemperatureScale.Fahrenheit, "°F", "f", "fahrenheit", "degf", "degreefahrenheit");
        AddTemperature(TemperatureScale.Kelvin, "K", "k", "kelvin", "degk");

        Add(Dimension.Volume, 1, "L", "l", "liter", "litre");
        Add(Dimension.Volume, 0.001, "mL", "ml", "milliliter", "millilitre");
        Add(Dimension.Volume, 0.01, "cL", "cl", "centiliter", "centilitre");
        Add(Dimension.Volume, 0.1, "dL", "dl", "deciliter", "decilitre");
        Add(Dimension.Volume, 1000, "m³", "m3", "cubicmeter", "cubicmetre");
        Add(Dimension.Volume, 0.001, "cm³", "cm3", "cubiccentimeter", "cubiccentimetre");
        Add(Dimension.Volume, 3.785411784, "gal", "gal", "gallon");
        Add(Dimension.Volume, 0.946352946, "qt", "qt", "quart");
        Add(Dimension.Volume, 0.473176473, "pt", "pt", "pint");
        Add(Dimension.Volume, 0.2365882365, "cup", "cup");
        Add(Dimension.Volume, 0.0295735295625, "fl oz", "floz", "fluidounce");
        Add(Dimension.Volume, 0.01478676478125, "tbsp", "tbsp", "tablespoon");
        Add(Dimension.Volume, 0.00492892159375, "tsp", "tsp", "teaspoon");

        Add(Dimension.Area, 1, "m²", "m2", "sqm", "squaremeter", "squaremetre");
        Add(Dimension.Area, 1e6, "km²", "km2", "sqkm", "squarekilometer", "squarekilometre");
        Add(Dimension.Area, 1e-4, "cm²", "cm2", "sqcm", "squarecentimeter", "squarecentimetre");
        Add(Dimension.Area, 1e-6, "mm²", "mm2", "sqmm");
        Add(Dimension.Area, 10000, "ha", "ha", "hectare");
        Add(Dimension.Area, 4046.8564224, "ac", "acre", "ac");
        Add(Dimension.Area, 0.09290304, "ft²", "ft2", "sqft", "squarefoot", "squarefeet");
        Add(Dimension.Area, 0.00064516, "in²", "in2", "sqin", "squareinch", "squareinches");
        Add(Dimension.Area, 0.83612736, "yd²", "yd2", "sqyd", "squareyard");
        Add(Dimension.Area, 2589988.110336, "mi²", "mi2", "sqmi", "squaremile");

        Add(Dimension.Data, 1, "B", "b", "byte", "bytes");
        Add(Dimension.Data, 0.125, "bit", "bit", "bits");
        Add(Dimension.Data, 1000, "KB", "kb", "kilobyte");
        Add(Dimension.Data, 1e6, "MB", "mb", "megabyte");
        Add(Dimension.Data, 1e9, "GB", "gb", "gigabyte");
        Add(Dimension.Data, 1e12, "TB", "tb", "terabyte");
        Add(Dimension.Data, 1e15, "PB", "pb", "petabyte");
        Add(Dimension.Data, 1024, "KiB", "kib", "kibibyte");
        Add(Dimension.Data, 1048576, "MiB", "mib", "mebibyte");
        Add(Dimension.Data, 1073741824, "GiB", "gib", "gibibyte");
        Add(Dimension.Data, 1099511627776, "TiB", "tib", "tebibyte");

        Add(Dimension.Time, 0.001, "ms", "ms", "millisecond");
        Add(Dimension.Time, 1, "s", "s", "sec", "second");
        Add(Dimension.Time, 60, "min", "min", "minute");
        Add(Dimension.Time, 3600, "h", "h", "hr", "hour");
        Add(Dimension.Time, 86400, "d", "d", "day");
        Add(Dimension.Time, 604800, "wk", "wk", "w", "week");

        Add(Dimension.Speed, 1, "m/s", "m/s", "mps", "meterspersecond", "metrespersecond");
        Add(Dimension.Speed, 1 / 3.6, "km/h", "kmh", "kph", "km/h", "kilometersperhour", "kilometresperhour");
        Add(Dimension.Speed, 0.44704, "mph", "mph", "mi/h", "milesperhour");
        Add(Dimension.Speed, 1852d / 3600, "kn", "knot", "kn", "kt");
        Add(Dimension.Speed, 0.3048, "ft/s", "fps", "ft/s", "feetpersecond");

        return units;
    }
}
