using System.Globalization;

namespace FlowDesk.Core.Palette;

/// <summary>
/// Pure palette time-zone lookup and conversion ("time in tokyo", "3pm london in local",
/// "15:00 utc to pst"). A requested clock time is interpreted on the current date in the source
/// zone, so daylight-saving rules of that date apply; abbreviation targets such as PST resolve to
/// the real Pacific zone (PST/PDT), never a fixed offset. Returns "ddd d MMM, time ABBR" formatted
/// with the supplied culture, or null when the text is not a recognizable time/city query.
/// </summary>
internal static class CityTimeZones
{
    private sealed record Place(string Iana, string Name, string Standard, string Daylight, string[] Aliases)
    {
        private TimeZoneInfo? zone;
        public TimeZoneInfo? Zone => zone ??= ResolveZone(Iana);
    }

    private static readonly Place[] Places =
    [
        new("UTC", "UTC", "UTC", "UTC", ["utc", "gmt", "z", "coordinated universal time", "greenwich time"]),
        new("Europe/London", "London", "GMT", "BST", ["london", "uk", "united kingdom", "england", "britain", "british time", "scotland", "edinburgh", "manchester", "bst"]),
        new("Europe/Dublin", "Dublin", "GMT", "IST", ["dublin", "ireland", "irish time", "cork"]),
        new("Europe/Lisbon", "Lisbon", "WET", "WEST", ["lisbon", "portugal", "wet", "west"]),
        new("Europe/Madrid", "Madrid", "CET", "CEST", ["madrid", "spain", "barcelona", "valencia"]),
        new("Europe/Paris", "Paris", "CET", "CEST", ["paris", "france", "marseille", "lyon"]),
        new("Europe/Brussels", "Brussels", "CET", "CEST", ["brussels", "belgium"]),
        new("Europe/Amsterdam", "Amsterdam", "CET", "CEST", ["amsterdam", "netherlands", "holland"]),
        new("Europe/Berlin", "Berlin", "CET", "CEST", ["berlin", "germany", "munich", "frankfurt", "hamburg", "cologne", "cet", "cest", "central european time"]),
        new("Europe/Zurich", "Zurich", "CET", "CEST", ["zurich", "switzerland", "geneva"]),
        new("Europe/Vienna", "Vienna", "CET", "CEST", ["vienna", "austria"]),
        new("Europe/Prague", "Prague", "CET", "CEST", ["prague", "czechia", "czech republic"]),
        new("Europe/Warsaw", "Warsaw", "CET", "CEST", ["warsaw", "poland"]),
        new("Europe/Copenhagen", "Copenhagen", "CET", "CEST", ["copenhagen", "denmark"]),
        new("Europe/Stockholm", "Stockholm", "CET", "CEST", ["stockholm", "sweden"]),
        new("Europe/Oslo", "Oslo", "CET", "CEST", ["oslo", "norway"]),
        new("Europe/Rome", "Rome", "CET", "CEST", ["rome", "italy", "milan", "naples"]),
        new("Europe/Athens", "Athens", "EET", "EEST", ["athens", "greece", "eet", "eest"]),
        new("Europe/Helsinki", "Helsinki", "EET", "EEST", ["helsinki", "finland"]),
        new("Europe/Kyiv", "Kyiv", "EET", "EEST", ["kyiv", "kiev", "ukraine"]),
        new("Europe/Bucharest", "Bucharest", "EET", "EEST", ["bucharest", "romania"]),
        new("Europe/Sofia", "Sofia", "EET", "EEST", ["sofia", "bulgaria"]),
        new("Europe/Budapest", "Budapest", "CET", "CEST", ["budapest", "hungary"]),
        new("Europe/Belgrade", "Belgrade", "CET", "CEST", ["belgrade", "serbia"]),
        new("Europe/Zagreb", "Zagreb", "CET", "CEST", ["zagreb", "croatia"]),
        new("Europe/Moscow", "Moscow", "MSK", "MSK", ["moscow", "russia", "msk"]),
        new("Europe/Istanbul", "Istanbul", "TRT", "TRT", ["istanbul", "turkey", "turkiye", "trt"]),
        new("Atlantic/Reykjavik", "Reykjavik", "GMT", "GMT", ["reykjavik", "iceland"]),
        new("America/New_York", "New York", "EST", "EDT", ["new york", "newyork", "nyc", "ny", "boston", "washington", "washington dc", "philadelphia", "miami", "atlanta", "est", "edt", "eastern time"]),
        new("America/Toronto", "Toronto", "EST", "EDT", ["toronto", "ottawa", "montreal", "ontario"]),
        new("America/Chicago", "Chicago", "CST", "CDT", ["chicago", "dallas", "houston", "austin", "san antonio", "texas", "cst", "cdt", "central time"]),
        new("America/Denver", "Denver", "MST", "MDT", ["denver", "colorado", "salt lake city", "mst", "mdt", "mountain time"]),
        new("America/Phoenix", "Phoenix", "MST", "MST", ["phoenix", "arizona", "scottsdale"]),
        new("America/Los_Angeles", "Los Angeles", "PST", "PDT", ["los angeles", "losangeles", "la", "san francisco", "seattle", "portland", "san diego", "sacramento", "pst", "pdt", "pacific time"]),
        new("America/Anchorage", "Anchorage", "AKST", "AKDT", ["anchorage", "alaska", "akst", "akdt"]),
        new("Pacific/Honolulu", "Honolulu", "HST", "HST", ["honolulu", "hawaii", "hst"]),
        new("America/Vancouver", "Vancouver", "PST", "PDT", ["vancouver", "british columbia"]),
        new("America/Edmonton", "Edmonton", "MST", "MDT", ["edmonton", "alberta", "calgary"]),
        new("America/Winnipeg", "Winnipeg", "CST", "CDT", ["winnipeg", "manitoba"]),
        new("America/Halifax", "Halifax", "AST", "ADT", ["halifax", "nova scotia", "ast", "adt", "atlantic time"]),
        new("America/St_Johns", "St. John's", "NST", "NDT", ["st johns", "stjohns", "newfoundland", "nst", "ndt"]),
        new("America/Mexico_City", "Mexico City", "CST", "CST", ["mexico city", "mexicocity", "mexico", "guadalajara", "monterrey"]),
        new("America/Sao_Paulo", "Sao Paulo", "BRT", "BRT", ["sao paulo", "saopaulo", "brazil", "rio de janeiro", "rio", "brasilia", "brt"]),
        new("America/Argentina/Buenos_Aires", "Buenos Aires", "ART", "ART", ["buenos aires", "buenosaires", "argentina", "art"]),
        new("America/Santiago", "Santiago", "CLT", "CLST", ["santiago", "chile", "clt"]),
        new("America/Lima", "Lima", "PET", "PET", ["lima", "peru", "pet"]),
        new("America/Bogota", "Bogota", "COT", "COT", ["bogota", "colombia", "cot"]),
        new("America/Caracas", "Caracas", "VET", "VET", ["caracas", "venezuela", "vet"]),
        new("America/Montevideo", "Montevideo", "UYT", "UYT", ["montevideo", "uruguay", "uyt"]),
        new("America/La_Paz", "La Paz", "BOT", "BOT", ["la paz", "lapaz", "bolivia", "bot"]),
        new("Asia/Tokyo", "Tokyo", "JST", "JST", ["tokyo", "japan", "osaka", "kyoto", "jst", "japan time"]),
        new("Asia/Seoul", "Seoul", "KST", "KST", ["seoul", "south korea", "korea", "kst", "busan", "korea time"]),
        new("Asia/Shanghai", "Shanghai", "CST", "CST", ["shanghai", "beijing", "china", "shenzhen", "guangzhou", "china time"]),
        new("Asia/Hong_Kong", "Hong Kong", "HKT", "HKT", ["hong kong", "hongkong", "hkt"]),
        new("Asia/Taipei", "Taipei", "CST", "CST", ["taipei", "taiwan"]),
        new("Asia/Singapore", "Singapore", "SGT", "SGT", ["singapore", "sgt"]),
        new("Asia/Kuala_Lumpur", "Kuala Lumpur", "MYT", "MYT", ["kuala lumpur", "kualalumpur", "malaysia", "myt"]),
        new("Asia/Manila", "Manila", "PHT", "PHT", ["manila", "philippines", "pht"]),
        new("Asia/Bangkok", "Bangkok", "ICT", "ICT", ["bangkok", "thailand", "ict", "vietnam", "hanoi", "ho chi minh", "ho chi minh city", "saigon"]),
        new("Asia/Jakarta", "Jakarta", "WIB", "WIB", ["jakarta", "indonesia", "wib"]),
        new("Asia/Kolkata", "Kolkata", "IST", "IST", ["kolkata", "calcutta", "delhi", "new delhi", "newdelhi", "mumbai", "bangalore", "bengaluru", "chennai", "hyderabad", "india", "ist", "india time"]),
        new("Asia/Karachi", "Karachi", "PKT", "PKT", ["karachi", "pakistan", "pkt", "islamabad", "lahore"]),
        new("Asia/Dhaka", "Dhaka", "BST", "BST", ["dhaka", "bangladesh", "bangladesh time"]),
        new("Asia/Kathmandu", "Kathmandu", "NPT", "NPT", ["kathmandu", "nepal", "npt"]),
        new("Asia/Colombo", "Colombo", "SLST", "SLST", ["colombo", "sri lanka", "srilanka", "slst"]),
        new("Asia/Dubai", "Dubai", "GST", "GST", ["dubai", "uae", "united arab emirates", "abu dhabi", "abudhabi", "gst"]),
        new("Asia/Riyadh", "Riyadh", "AST", "AST", ["riyadh", "saudi arabia", "saudi"]),
        new("Asia/Qatar", "Doha", "AST", "AST", ["doha", "qatar"]),
        new("Asia/Tehran", "Tehran", "IRST", "IRDT", ["tehran", "iran", "irst", "irdt"]),
        new("Asia/Jerusalem", "Jerusalem", "IST", "IDT", ["jerusalem", "israel", "tel aviv", "telaviv", "idt"]),
        new("Asia/Baghdad", "Baghdad", "AST", "AST", ["baghdad", "iraq"]),
        new("Africa/Cairo", "Cairo", "EET", "EEST", ["cairo", "egypt"]),
        new("Africa/Johannesburg", "Johannesburg", "SAST", "SAST", ["johannesburg", "cape town", "capetown", "south africa", "sast"]),
        new("Africa/Lagos", "Lagos", "WAT", "WAT", ["lagos", "nigeria", "wat"]),
        new("Africa/Nairobi", "Nairobi", "EAT", "EAT", ["nairobi", "kenya", "eat", "tanzania", "addis ababa", "addisababa", "ethiopia"]),
        new("Africa/Casablanca", "Casablanca", "+01", "+01", ["casablanca", "morocco"]),
        new("Africa/Accra", "Accra", "GMT", "GMT", ["accra", "ghana"]),
        new("Africa/Algiers", "Algiers", "CET", "CET", ["algiers", "algeria"]),
        new("Africa/Tunis", "Tunis", "CET", "CET", ["tunis", "tunisia"]),
        new("Australia/Sydney", "Sydney", "AEST", "AEDT", ["sydney", "melbourne", "canberra", "australia", "aest", "aedt", "australian eastern time"]),
        new("Australia/Brisbane", "Brisbane", "AEST", "AEST", ["brisbane", "queensland", "gold coast"]),
        new("Australia/Adelaide", "Adelaide", "ACST", "ACDT", ["adelaide", "south australia", "acst", "acdt"]),
        new("Australia/Perth", "Perth", "AWST", "AWST", ["perth", "western australia", "awst"]),
        new("Australia/Darwin", "Darwin", "ACST", "ACST", ["darwin", "northern territory"]),
        new("Australia/Hobart", "Hobart", "AEST", "AEDT", ["hobart", "tasmania"]),
        new("Pacific/Auckland", "Auckland", "NZST", "NZDT", ["auckland", "new zealand", "wellington", "christchurch", "nzst", "nzdt"]),
        new("Pacific/Fiji", "Suva", "FJT", "FJT", ["fiji", "suva", "fjt"])
    ];

    private static readonly Dictionary<string, Place> Lookup = BuildLookup();
    private static readonly (string Alias, Place Place)[] Suffixes = BuildSuffixes();
    private static readonly Dictionary<string, TimeZoneInfo?> ZoneCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<Place?> LocalPlace = new(FindLocalPlace);

    private static readonly string[] NowPrefixes =
    [
        "what time is it in ",
        "what is the time in ",
        "whats the time in ",
        "current time in ",
        "what time in ",
        "time in ",
        "time at "
    ];

    public static string? Evaluate(string input, CultureInfo culture, DateTimeOffset now)
    {
        var text = Normalize(input);
        if (text.Length == 0) return null;

        foreach (var prefix in NowPrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) continue;
            return TryResolveTarget(text[prefix.Length..], out var place, out var zone)
                ? Format(TimeZoneInfo.ConvertTime(now, zone), place, zone, culture) : null;
        }

        if (text.EndsWith(" time", StringComparison.Ordinal))
        {
            var placeText = text[..^5].Trim();
            if (placeText.Length > 0 && TryResolveTarget(placeText, out var place, out var zone))
                return Format(TimeZoneInfo.ConvertTime(now, zone), place, zone, culture);
        }

        if (!TrySplitTarget(text, out var left, out var targetText)) return null;
        if (!TryResolveTarget(targetText, out var targetPlace, out var targetZone)) return null;
        if (!TrySplitSource(left, out var timeOfDay, out var sourcePlace)) return null;
        if (timeOfDay is null) return null;
        var sourceZone = sourcePlace?.Zone ?? TimeZoneInfo.Local;
        if (sourcePlace is not null && sourcePlace.Zone is null) return null;

        // The clock time belongs to the current date in the source zone, not in UTC or the local zone.
        var sourceDate = TimeZoneInfo.ConvertTime(now, sourceZone).Date;
        var sourceLocal = DateTime.SpecifyKind(sourceDate + timeOfDay.Value, DateTimeKind.Unspecified);
        var instant = ToInstant(sourceZone, sourceLocal);
        var targetLocal = TimeZoneInfo.ConvertTime(instant, targetZone);
        return Format(targetLocal, targetPlace, targetZone, culture);
    }

    private static bool TrySplitTarget(string text, out string left, out string right)
    {
        left = "";
        right = "";
        int index = text.LastIndexOf(" in ", StringComparison.Ordinal);
        int to = text.LastIndexOf(" to ", StringComparison.Ordinal);
        if (to > index) index = to;
        if (index <= 0) return false;
        left = text[..index].Trim();
        right = text[(index + 4)..].Trim();
        return left.Length > 0 && right.Length > 0;
    }

    private static bool TrySplitSource(string text, out TimeSpan? timeOfDay, out Place? place)
    {
        timeOfDay = null;
        place = null;
        text = text.Trim();
        if (text.Length == 0) return false;

        if (TryFindSuffixPlace(text, out var suffixPlace, out int start))
        {
            var remainder = text[..start].Trim();
            if (remainder.Length == 0) return false;
            if (!TryParseTime(remainder, out var parsed)) return false;
            timeOfDay = parsed;
            place = suffixPlace;
            return true;
        }

        if (!TryParseTime(text, out var time)) return false;
        timeOfDay = time;
        return true;
    }

    private static bool TryParseTime(string text, out TimeSpan time)
    {
        time = default;
        text = text.Trim();
        if (text.StartsWith("at ", StringComparison.Ordinal)) text = text[3..].Trim();
        if (text == "noon") { time = new TimeSpan(12, 0, 0); return true; }
        if (text == "midnight") { time = TimeSpan.Zero; return true; }

        int position = 0;
        int hour = 0;
        int digits = 0;
        while (position < text.Length && char.IsDigit(text[position]) && digits < 2)
        {
            hour = hour * 10 + (text[position] - '0');
            position++;
            digits++;
        }
        if (digits == 0) return false;

        int minute = 0;
        int second = 0;
        if (position < text.Length && text[position] == ':')
        {
            position++;
            if (!TryReadTwoDigits(text, ref position, out minute)) return false;
            if (position < text.Length && text[position] == ':')
            {
                position++;
                if (!TryReadTwoDigits(text, ref position, out second)) return false;
            }
        }

        bool pm = false;
        var suffix = text[position..].Trim();
        if (suffix is "pm" or "p") pm = true;
        else if (suffix is not ("am" or "a" or "")) return false;
        if (minute > 59 || second > 59) return false;

        if (suffix.Length > 0)
        {
            if (hour is < 1 or > 12) return false;
            if (hour == 12) hour = 0;
            if (pm) hour += 12;
        }
        else if (hour > 23) return false;

        time = new TimeSpan(hour, minute, second);
        return true;
    }

    private static bool TryReadTwoDigits(string text, ref int position, out int value)
    {
        value = 0;
        if (position + 1 >= text.Length || !char.IsDigit(text[position]) || !char.IsDigit(text[position + 1]))
            return false;
        value = (text[position] - '0') * 10 + (text[position + 1] - '0');
        position += 2;
        return true;
    }

    private static DateTimeOffset ToInstant(TimeZoneInfo zone, DateTime local)
    {
        if (zone.IsInvalidTime(local))
        {
            var before = zone.GetUtcOffset(local.AddHours(-6));
            var after = zone.GetUtcOffset(local.AddHours(6));
            local = local.Add(after - before);
        }
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static string Format(DateTimeOffset local, Place? place, TimeZoneInfo zone, CultureInfo culture)
    {
        var date = local.ToString("ddd d MMM", culture);
        var time = local.ToString("t", culture);
        var abbreviation = Abbreviation(place, zone, local);
        return abbreviation.Length == 0 ? $"{date}, {time}" : $"{date}, {time} {abbreviation}";
    }

    private static string Abbreviation(Place? place, TimeZoneInfo zone, DateTimeOffset local)
    {
        place ??= LocalPlace.Value;
        if (place is not null) return zone.IsDaylightSavingTime(local) ? place.Daylight : place.Standard;
        return zone.Id is "UTC" or "Etc/UTC" ? "UTC" : "";
    }

    private static bool TryResolveTarget(string text, out Place? place, out TimeZoneInfo zone)
    {
        place = null;
        zone = TimeZoneInfo.Local;
        var compact = Compact(text);
        if (compact is "local" or "localtime" or "here" or "mytime" or "mytimezone" or "thisdevice")
            return true;
        if (!Lookup.TryGetValue(compact, out place)) return false;
        if (place.Zone is not { } found) { place = null; return false; }
        zone = found;
        return true;
    }

    private static bool TryFindSuffixPlace(string text, out Place place, out int start)
    {
        foreach (var (alias, candidate) in Suffixes)
        {
            if (alias.Length > text.Length) continue;
            int index = text.Length - alias.Length;
            if (index > 0 && text[index - 1] != ' ') continue;
            if (string.CompareOrdinal(text, index, alias, 0, alias.Length) != 0) continue;
            place = candidate;
            start = index;
            return true;
        }
        place = null!;
        start = 0;
        return false;
    }

    private static Dictionary<string, Place> BuildLookup()
    {
        var lookup = new Dictionary<string, Place>(StringComparer.Ordinal);
        foreach (var place in Places)
            foreach (var alias in place.Aliases)
                lookup.TryAdd(Compact(alias), place);
        return lookup;
    }

    private static (string Alias, Place Place)[] BuildSuffixes()
    {
        var suffixes = new List<(string, Place)>();
        foreach (var place in Places)
            foreach (var alias in place.Aliases)
            {
                suffixes.Add((alias, place));
                var compact = Compact(alias);
                if (compact != alias) suffixes.Add((compact, place));
            }
        return suffixes.OrderByDescending(s => s.Item1.Length).ToArray();
    }

    private static string Compact(string text) => text.Replace(" ", "").Replace("-", "");

    private static TimeZoneInfo? ResolveZone(string iana)
    {
        lock (ZoneCache)
            if (ZoneCache.TryGetValue(iana, out var cached)) return cached;

        TimeZoneInfo? zone = null;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(iana, out var found))
            zone = found;
        else if (TimeZoneInfo.TryConvertIanaIdToWindowsId(iana, out var windows) &&
                 TimeZoneInfo.TryFindSystemTimeZoneById(windows, out found))
            zone = found;

        lock (ZoneCache) ZoneCache[iana] = zone;
        return zone;
    }

    private static Place? FindLocalPlace()
    {
        var id = TimeZoneInfo.Local.Id;
        if (!id.Contains('/') && TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)) id = iana;
        return Places.FirstOrDefault(p => string.Equals(p.Iana, id, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string input)
    {
        var buffer = new System.Text.StringBuilder(input.Length);
        foreach (var raw in input.Trim())
        {
            char c = char.ToLowerInvariant(raw);
            if (c is '\t' or '\r' or '\n' or '\u00A0' or '-') c = ' ';
            if (c is '.' or '\'' or '"' or ',' or '?' or '!' or '(' or ')') continue;
            if (char.IsWhiteSpace(c))
            {
                if (buffer.Length > 0 && buffer[^1] != ' ') buffer.Append(' ');
                continue;
            }
            buffer.Append(c);
        }
        return buffer.ToString().Trim();
    }
}
