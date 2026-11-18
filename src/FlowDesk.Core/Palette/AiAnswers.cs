using System.Runtime.CompilerServices;

namespace FlowDesk.Core.Palette;

public enum AiProviderKind { Anthropic, OpenAICompatible }
public sealed record AiOptions
{
    public AiProviderKind Provider { get; init; }
    public string BaseUrl { get; init; } = "https://api.openai.com/v1";
    public string Model { get; init; } = "claude-opus-5-5";
    public bool ShowFallback { get; init; } = true;
}
public interface IAiCredentialStore
{
    string? Read(AiProviderKind provider);
    void Save(AiProviderKind provider, string key);
    void Remove(AiProviderKind provider);
}
public sealed record AiAvailability(AiOptions Options, bool HasKey)
{
    public bool Configured => AiRules.Validate(Options) is null && (HasKey || AiRules.AllowsEmptyKey(Options));
}
public static class AiRules
{
    public const string SystemPrompt = "Give concise, helpful answers for a desktop launcher. Prefer plain text; use simple Markdown only when useful.";
    public static bool AllowsEmptyKey(AiOptions options) => options.Provider == AiProviderKind.OpenAICompatible &&
        Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback && uri.Scheme is "http" or "https";
    public static string? Validate(AiOptions options)
    {
        if (!Enum.IsDefined(options.Provider)) return "Choose an AI provider.";
        if (string.IsNullOrWhiteSpace(options.Model) || options.Model.Length > 200 || options.Model.Any(char.IsControl)) return "Enter a model name.";
        if (options.Provider == AiProviderKind.OpenAICompatible &&
            (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
             (uri.Scheme == "http" && !uri.IsLoopback) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0))
            return "Use an HTTPS base URL, or HTTP for localhost. Do not put a key in the URL.";
        return null;
    }
    public static string ChatUrl(AiProviderKind provider) => provider == AiProviderKind.Anthropic ? "https://claude.ai/new" : "https://chatgpt.com/";
    public static string? StopNote(string reason)
    {
        string normalized = reason.Replace("_", "").Replace("-", "").ToLowerInvariant();
        if (normalized.Contains("maxtokens")) return "\n\nThe answer reached its length limit.";
        if (normalized.Contains("refusal")) return "\n\nThe provider declined to answer this question.";
        return null;
    }
    public static string ChatUrl(AiOptions options)
    {
        if (options.Provider == AiProviderKind.Anthropic) return ChatUrl(options.Provider);
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)) return "https://chatgpt.com/";
        return uri.Host.ToLowerInvariant() switch
        {
            "openrouter.ai" or "api.openrouter.ai" => "https://openrouter.ai/chat",
            "api.deepseek.com" or "deepseek.com" => "https://chat.deepseek.com/",
            "api.openai.com" => "https://chatgpt.com/",
            _ => uri.GetLeftPart(UriPartial.Authority)
        };
    }
}
public sealed class AiAnswerException(string message) : Exception(message);
public sealed record AiChunk(string Text);
public interface IAiAnswerProvider
{
    IAsyncEnumerable<AiChunk> StreamAsync(string question, CancellationToken token);
}

public sealed class AiCommandSource(Func<AiAvailability> available, Func<string, Task> ask) : IModeCommandSource
{
    public const double FallbackScore = -20000;
    public string Name => "AI answers";
    public PaletteMode Mode => PaletteMode.Ai;
    public IEnumerable<CommandMatch> Query(string query) => Query(query, PaletteMode.Commands);
    public IEnumerable<CommandMatch> Query(string query, PaletteMode mode)
    {
        if (mode is not (PaletteMode.Commands or PaletteMode.Ai)) yield break;
        var configuration = available();
        if (!configuration.Configured || (mode == PaletteMode.Commands && !configuration.Options.ShowFallback) || string.IsNullOrWhiteSpace(query)) yield break;
        string question = query.Trim();
        yield return new(new("ai:ask", "Ask AI: " + question, configuration.Options.Model + " · Sent only on Enter", CommandKind.Ai, "\uE945",
            () => ask(question), KeepsPaletteOpen: true, RecordUsage: false, ActionHint: "Ask AI"), mode == PaletteMode.Ai ? 1 : FallbackScore, []);
    }
}

/// <summary>One explicit submission at a time. Cancellation is immediate for the UI; an uncooperative
/// old provider can never change a newer answer. No exception object from a transport reaches the UI.</summary>
public sealed class AiAnswerSession(TimeSpan? timeout = null)
{
    private CancellationTokenSource? request;
    private long generation;
    public event Action? Changed;
    public string Question { get; private set; } = "";
    public string Answer { get; private set; } = "";
    public string Status { get; private set; } = "";
    public bool Visible { get; private set; }
    public bool Streaming { get; private set; }
    public async Task AskAsync(IAiAnswerProvider provider, string question)
    {
        Cancel();
        long expected = ++generation;
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
        request = cancellation; Question = question; Answer = ""; Status = "Answering… · Esc to cancel"; Visible = true; Streaming = true; Changed?.Invoke();
        try
        {
            await foreach (var chunk in provider.StreamAsync(question, cancellation.Token))
            {
                if (generation != expected) return;
                cancellation.Token.ThrowIfCancellationRequested();
                Answer += chunk.Text;
                if (Answer.Length > 64000) { Answer = Answer[..64000] + "\n\nAnswer truncated."; break; }
                Changed?.Invoke();
            }
            if (generation == expected) Status = Answer.Length == 0 ? "The provider returned no answer." : "Enter · Copy answer   Esc · Results";
        }
        catch (OperationCanceledException) { if (generation == expected) Status = "The request timed out after 60 seconds. Try again."; }
        catch (AiAnswerException ex) { if (generation == expected) Status = ex.Message; }
        catch (Exception) { if (generation == expected) Status = "The answer could not be loaded. Check AI settings and try again."; }
        finally
        {
            cancellation.Cancel();
            if (generation == expected) { request = null; Streaming = false; Changed?.Invoke(); }
        }
    }
    public void Cancel()
    {
        if (!Streaming) return;
        ++generation; request?.Cancel(); request = null; Streaming = false; Status = "Cancelled · Esc to return to results"; Changed?.Invoke();
    }
    public void Close() { Cancel(); Visible = false; Changed?.Invoke(); }
}

/// <summary>Incremental SSE framing, bounded against malformed endpoints; accepts arbitrary read boundaries.</summary>
public sealed class AiSseParser
{
    private readonly System.Text.StringBuilder line = new(), data = new();
    public IReadOnlyList<string> Feed(string text)
    {
        var events = new List<string>();
        foreach (char c in text)
        {
            if (c == '\n') Process(events);
            else { line.Append(c); if (line.Length > 65536) throw new AiAnswerException("The provider returned an invalid stream."); }
        }
        return events;
    }
    public IReadOnlyList<string> Complete()
    {
        var events = new List<string>(); if (line.Length > 0) Process(events); Flush(events); return events;
    }
    private void Process(List<string> events)
    {
        string text = line.ToString().TrimEnd('\r'); line.Clear();
        if (text.Length == 0) Flush(events);
        else if (text.StartsWith("data:", StringComparison.Ordinal))
        {
            string value = text[5..]; if (value.StartsWith(' ')) value = value[1..];
            if (data.Length > 0) data.Append('\n'); data.Append(value);
            if (data.Length > 65536) throw new AiAnswerException("The provider returned an invalid stream.");
        }
    }
    private void Flush(List<string> events) { if (data.Length > 0) { events.Add(data.ToString()); data.Clear(); } }
    public static async IAsyncEnumerable<string> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken token)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 2048, leaveOpen: true);
        var parser = new AiSseParser(); char[] buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            foreach (string item in parser.Feed(new string(buffer, 0, count))) yield return item;
        foreach (string item in parser.Complete()) yield return item;
    }
}
