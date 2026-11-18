using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace FlowDesk.Core.Palette;

public static class AiRequestBuilder
{
    // Credentials never enter request parameters/bodies, settings, error strings or their ToString().
    public static MessageCreateParams Anthropic(AiOptions options, string question) => new()
    {
        Model = options.Model, MaxTokens = 2048, System = AiRules.SystemPrompt,
        OutputConfig = new OutputConfig { Effort = Effort.Low },
        Messages = [new UserMessageParam { Content = question }]
    };
    public static string OpenAI(AiOptions options, string question) => JsonSerializer.Serialize(new
    {
        model = options.Model, stream = true, max_tokens = 2048,
        messages = new[] { new { role = "system", content = AiRules.SystemPrompt }, new { role = "user", content = question } }
    });
    public static IReadOnlyList<AiChunk> ParseOpenAI(string data)
    {
        try
        {
            using var json = JsonDocument.Parse(data); var root = json.RootElement;
            if (root.TryGetProperty("error", out _)) throw new AiAnswerException("The provider rejected the request. Check your model and AI settings.");
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return [];
            var choice = choices[0]; var result = new List<AiChunk>();
            if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                result.Add(new(content.GetString() ?? ""));
            if (choice.TryGetProperty("finish_reason", out var reason))
            {
                if (reason.GetString() == "length") result.Add(new("\n\nThe answer reached its length limit."));
                if (reason.GetString() == "content_filter") result.Add(new("\n\nThe provider declined to answer this question."));
            }
            return result;
        }
        catch (JsonException) { throw new AiAnswerException("The provider returned an invalid stream."); }
    }
}

public sealed class PaletteAiProvider(AiOptions options, IAiCredentialStore credentials, HttpClient? http = null) : IAiAnswerProvider
{
    private static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    public async IAsyncEnumerable<AiChunk> StreamAsync(string question, [EnumeratorCancellation] CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60)); token = timeout.Token;
        if (AiRules.Validate(options) is { } error) throw new AiAnswerException(error);
        string key;
        try { key = credentials.Read(options.Provider) ?? ""; }
        catch (Exception) { throw new AiAnswerException("Your saved key could not be read. Save it again in AI settings."); }
        if (key.Length == 0 && !AiRules.AllowsEmptyKey(options)) throw new AiAnswerException("Save an API key in AI settings first.");
        // Catch transport failures at MoveNext, so no SDK exception (which might contain headers)
        // can escape to a caller, logger or crash reporter. Never retain an inner exception.
        var source = options.Provider == AiProviderKind.Anthropic ? AnthropicStream(question, key, token) : OpenAIStream(question, key, token);
        await using var iterator = source.GetAsyncEnumerator(token);
        while (true)
        {
            bool next;
            try { next = await iterator.MoveNextAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (AiAnswerException) { throw; }
            catch (Exception ex) { throw SafeError(ex); }
            if (!next) break;
            yield return iterator.Current;
        }
    }
    private async IAsyncEnumerable<AiChunk> AnthropicStream(string question, string key, [EnumeratorCancellation] CancellationToken token)
    {
        using var sdkHttp = new HttpClient(new ForwardHttp(http ?? SharedHttp)) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = new AnthropicClient { ApiKey = key, MaxRetries = 0, BaseUrl = "https://api.anthropic.com", HttpClient = sdkHttp, Timeout = TimeSpan.FromSeconds(60), OpenTelemetry = false };
        await foreach (var item in client.Messages.CreateStreaming(AiRequestBuilder.Anthropic(options, question), cancellationToken: token))
        {
            if (item.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text)) yield return new(text.Text);
            if (item.TryPickDelta(out var messageDelta))
            {
                string stop = messageDelta.Delta.StopReason?.ToString() ?? "";
                if (AiRules.StopNote(stop) is { } note) yield return new(note);
            }
        }
    }
    private async IAsyncEnumerable<AiChunk> OpenAIStream(string question, string key, [EnumeratorCancellation] CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.BaseUrl.TrimEnd('/') + "/chat/completions");
        request.Content = new StringContent(AiRequestBuilder.OpenAI(options, question), Encoding.UTF8, "application/json");
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await (http ?? SharedHttp).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw StatusError((int)response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await foreach (string data in AiSseParser.ReadAsync(stream, token).ConfigureAwait(false))
        {
            if (data.Trim() == "[DONE]") yield break;
            foreach (var chunk in AiRequestBuilder.ParseOpenAI(data)) yield return chunk;
        }
    }
    private static AiAnswerException SafeError(Exception error)
    {
        if (error is Anthropic.Exceptions.AnthropicApiException api) return StatusError((int)api.StatusCode);
        return new(error is HttpRequestException ? "Could not reach the provider. Check your connection and base URL." : "The provider could not answer. Check the model and AI settings.");
    }
    private static AiAnswerException StatusError(int status) => new(status switch
    {
        401 or 403 => "The provider did not accept your API key. Save a valid key in AI settings.",
        429 => "The provider is busy or your quota is reached. Wait a moment and try again.",
        >= 500 => "The provider is unavailable. Try again later.",
        _ => "The provider rejected the request. Check your model and AI settings."
    });
    // Disposing a per-request SDK client must not dispose the shared transport or a test's fake.
    private sealed class ForwardHttp(HttpClient client) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            // The SDK-facing HttpClient has already marked this message as sent. A second
            // HttpClient cannot send that same instance; preserve its headers/body in a new message.
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
            foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (request.Content is not null)
            {
                forwarded.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false));
                foreach (var header in request.Content.Headers) forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return await client.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        }
    }
}
