namespace FlowDesk.Core.Palette;

public enum WebEngine { Google, Bing, DuckDuckGo, Brave, Custom }

public static class WebSearch
{
    public const double FallbackScore = -1000;
    public static bool IsValidTemplate(string template)
    {
        const string marker = "flowdeskquerymarker";
        if (!template.Contains("%s", StringComparison.Ordinal) || template.Contains(marker, StringComparison.OrdinalIgnoreCase)) return false;
        var uri = Parse(template.Replace("%s", marker, StringComparison.Ordinal));
        return uri?.Scheme == Uri.UriSchemeHttps && !uri.Authority.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    public static Uri? Build(WebEngine engine, string template, string query)
    {
        string escaped = Uri.EscapeDataString(query);
        return Parse(engine switch
        {
            WebEngine.Bing => "https://www.bing.com/search?q=" + escaped,
            WebEngine.DuckDuckGo => "https://duckduckgo.com/?q=" + escaped,
            WebEngine.Brave => "https://search.brave.com/search?q=" + escaped,
            WebEngine.Custom => IsValidTemplate(template) ? template.Replace("%s", escaped, StringComparison.Ordinal) : "",
            _ => "https://www.google.com/search?q=" + escaped
        });
    }

    public static Uri? Address(string input)
    {
        input = input.Trim();
        bool explicitScheme = input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        var uri = Parse(explicitScheme ? input : "https://" + input);
        return uri is not null && (explicitScheme || (uri.HostNameType == UriHostNameType.Dns && uri.Host.Contains('.'))) ? uri : null;
    }

    private static Uri? Parse(string value) => !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\') &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" &&
        uri.HostNameType is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6 && uri.UserInfo.Length == 0 ? uri : null;
}

public sealed record WebOptions(bool Enabled, bool Fallback, WebEngine Engine, string Template);

public sealed class WebCommandSource(Func<WebOptions> settings, Func<string, Task> launch) : IModeCommandSource
{
    public string Name => "Web search";
    public PaletteMode Mode => PaletteMode.Web;
    public IEnumerable<CommandMatch> Query(string query) => Query(query, PaletteMode.Commands);
    public IEnumerable<CommandMatch> Query(string query, PaletteMode mode)
    {
        var options = settings();
        query = query.Trim();
        if (!options.Enabled || query.Length == 0) yield break;
        if (WebSearch.Address(query) is { } address)
        {
            yield return Row("web:open", "Open " + query, "Open in default browser", address, 9_000);
            yield break;
        }
        if (mode != Mode && !options.Fallback) yield break;
        string engine = options.Engine == WebEngine.Custom ? "custom engine" : options.Engine.ToString();
        var url = WebSearch.Build(options.Engine, options.Template, query);
        if (url is not null)
            yield return Row("web:search", $"Search {(mode == Mode ? engine : "the web")} for ‘{query}’", "Open in default browser", url, WebSearch.FallbackScore);
        else if (mode == Mode)
            yield return new(new("web:invalid", "Custom search URL is invalid", "Edit the search URL in Settings", CommandKind.Web,
                "\uE774", () => Task.CompletedTask, RecordUsage: false, NamedActions: []), 1, []);
    }

    private CommandMatch Row(string id, string title, string subtitle, Uri url, double score) =>
        new(new(id, title, subtitle, CommandKind.Web, "\uE774", () => launch(url.AbsoluteUri), RecordUsage: false), score, []);
}
