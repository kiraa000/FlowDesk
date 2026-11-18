using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteAiTests
{
    private static AiOptions Local => new() { Provider = AiProviderKind.OpenAICompatible, BaseUrl = "http://localhost:11434/v1", Model = "test-model" };
    [Theory] [InlineData("http://localhost:11434/v1", true)] [InlineData("http://127.0.0.1:1234/v1", true)]
    [InlineData("https://api.openai.com/v1", false)] [InlineData("http://localhost.evil/v1", false)]
    public void EmptyKeyIsAllowedOnlyForExplicitLoopbackEndpoints(string url, bool expected) => Assert.Equal(expected, AiRules.AllowsEmptyKey(Local with { BaseUrl = url }));
    [Theory] [InlineData("http://example.com/v1")] [InlineData("https://user:secret@example.com/v1")] [InlineData("https://example.com/v1?key=secret")] [InlineData("file:///D:/v1")]
    public void UnsafeEndpointsAreRejectedWithoutEchoingSecrets(string url)
    { string? error = AiRules.Validate(Local with { BaseUrl = url }); Assert.NotNull(error); Assert.DoesNotContain("secret", error); }
    [Fact] public void RequestsContainOnlyOneQuestionAndNoKeyThinkingBudgetOrSamplingParameters()
    {
        var request = AiRequestBuilder.Anthropic(new(), "a question");
        Assert.Equal(2048, request.MaxTokens); Assert.Null(request.Thinking); Assert.NotNull(request.OutputConfig);
        Assert.Single(request.Messages); Assert.DoesNotContain("secret-test-key", request.ToString());
        string json = AiRequestBuilder.OpenAI(Local, "question with \"quotes\"");
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean()); Assert.Equal(2, document.RootElement.GetProperty("messages").GetArrayLength());
        Assert.DoesNotContain("api_key", json); Assert.DoesNotContain("temperature", json); Assert.DoesNotContain("secret-test-key", json);
    }
    [Fact] public async Task TypingAndPointerCannotCallProviderOnlyDeliberateEnterCan()
    {
        var provider = new FixedProvider("answer"); var session = new AiAnswerSession();
        var source = new AiCommandSource(() => new(new(), true), question => session.AskAsync(provider, question));
        var row = Assert.Single(source.Query("why", PaletteMode.Ai)); Assert.Equal(0, provider.Calls); Assert.False(session.Visible);
        var executor = new PaletteActionExecutor(() => TimeSpan.Zero);
        Assert.Equal(PaletteExecution.Invalid, await executor.InvokeAsync(row.Item, 0, PaletteInvocation.Pointer, _ => { }, _ => { }));
        Assert.Equal(0, provider.Calls);
        Assert.Equal(PaletteExecution.Executed, await executor.InvokeAsync(row.Item, 0, PaletteInvocation.Enter, _ => { }, _ => { }));
        Assert.Equal(1, provider.Calls); Assert.Equal("answer", session.Answer); Assert.False(session.Streaming);
    }
    [Fact] public void PrefixFallbackAndConfigurationRulesDoNotReadCredentialsOrSendRequests()
    {
        var available = new AiAvailability(new(), false);
        var source = new AiCommandSource(() => available, _ => throw new Exception("Must not send while typing"));
        Assert.Empty(source.Query("why")); available = available with { HasKey = true };
        Assert.Single(source.Query("why")); available = available with { Options = available.Options with { ShowFallback = false } };
        Assert.Empty(source.Query("why")); Assert.Single(source.Query("why", PaletteMode.Ai)); Assert.Empty(source.Query("", PaletteMode.Ai));
        Assert.Equal(PaletteMode.Ai, PalettePrefixes.Match("AI hello")?.Mode);
        var apps = new SnapshotCommandSource("apps", () => Enumerable.Range(0, 90).Select(i => new CommandItem("app" + i, "why " + i, "", CommandKind.App, "", () => Task.CompletedTask)).ToArray());
        available = available with { Options = available.Options with { ShowFallback = true } };
        var results = new CommandSearch([apps, source], new("not-written.json"), (_, _) => { }).Query("why");
        Assert.Equal(80, results.Count); Assert.Equal(CommandKind.Ai, results[^1].Item.Kind);
    }
    [Fact] public void SseParserAcceptsEveryPossibleMidLineBoundaryAndDone()
    {
        const string text = ": ping\r\ndata: {\"choices\":[{\"delta\":{\"content\":\"héllo\"}}]}\r\n\r\ndata: [DONE]\n\n";
        for (int at = 0; at <= text.Length; at++)
        {
            var parser = new AiSseParser(); var data = parser.Feed(text[..at]).Concat(parser.Feed(text[at..])).Concat(parser.Complete()).ToArray();
            Assert.Equal(2, data.Length); Assert.Equal("héllo", Assert.Single(AiRequestBuilder.ParseOpenAI(data[0])).Text); Assert.Equal("[DONE]", data[1]);
        }
        Assert.Throws<AiAnswerException>(() => AiRequestBuilder.ParseOpenAI("{\"error\":{\"message\":\"secret-test-key\"}}"));
        Assert.Throws<AiAnswerException>(() => new AiSseParser().Feed(new string('x', 65537)));
    }
    [Fact] public async Task OpenAiTransportUsesInjectedHttpAndStopsAtDoneWithoutNetwork()
    {
        var keys = new FakeKeys("secret-test-key"); string? body = null;
        using var http = new HttpClient(new FakeHandler(async (request, token) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal("secret-test-key", request.Headers.Authorization?.Parameter);
            Assert.Equal("https://example.invalid/v1/chat/completions", request.RequestUri?.ToString());
            body = await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\ndata: [DONE]\n\ndata: invalid\n\n", Encoding.UTF8) };
        }));
        var provider = new PaletteAiProvider(Local with { BaseUrl = "https://example.invalid/v1" }, keys, http);
        var chunks = new List<AiChunk>(); await foreach (var chunk in provider.StreamAsync("test", CancellationToken.None)) chunks.Add(chunk);
        Assert.Equal("hello", Assert.Single(chunks).Text); Assert.DoesNotContain("secret-test-key", body);
    }
    [Theory] [InlineData(401, "key")] [InlineData(429, "quota")] [InlineData(500, "unavailable")]
    public async Task ProviderErrorsNeverIncludeResponseBodiesHeadersKeysOrInnerExceptions(int status, string hint)
    {
        using var http = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("secret-test-key") })));
        var error = await Assert.ThrowsAsync<AiAnswerException>(async () => { await foreach (var _ in new PaletteAiProvider(Local, new FakeKeys("secret-test-key"), http).StreamAsync("q", CancellationToken.None)) { } });
        Assert.Contains(hint, error.Message); Assert.DoesNotContain("secret-test-key", error.ToString()); Assert.Null(error.InnerException);
    }
    [Fact] public async Task NetworkExceptionIsSanitizedEvenWhenTransportIncludesTheAuthorizationHeader()
    {
        using var http = new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("Bearer secret-test-key")));
        var error = await Assert.ThrowsAsync<AiAnswerException>(async () => { await foreach (var _ in new PaletteAiProvider(Local, new FakeKeys("secret-test-key"), http).StreamAsync("q", CancellationToken.None)) { } });
        Assert.DoesNotContain("secret-test-key", error.ToString()); Assert.Null(error.InnerException);
    }
    [Fact] public async Task OfficialAnthropicSdkStreamsThroughFakeTransportWithExplicitKeyAndLowEffort()
    {
        int calls = 0; string? sentUrl = null, sentKey = null, sentBody = null;
        using var http = new HttpClient(new FakeHandler(async (request, token) =>
        {
            calls++; sentUrl = request.RequestUri?.ToString(); sentKey = request.Headers.Authorization?.Parameter;
            sentBody = await request.Content!.ReadAsStringAsync(token);
            const string sse = "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"SDK answer\"}}\n\nevent: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":2}}\n\nevent: message_stop\ndata: {\"type\":\"message_stop\"}\n\n";
            return new(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };
        }));
        // Validate the fixture directly too: a malformed fake event must not be mistaken for
        // a production transport failure hidden by the intentional error-sanitizing boundary.
        using var sdk = new Anthropic.AnthropicClient { ApiKey = "secret-test-key", BaseUrl = "https://api.anthropic.com", HttpClient = http, MaxRetries = 0 };
        await foreach (var item in sdk.Messages.CreateStreaming(AiRequestBuilder.Anthropic(new(), "q"))) { _ = item; }
        calls = 0;
        for (int i = 0; i < 2; i++)
        {
            var chunks = new List<AiChunk>();
            await foreach (var chunk in new PaletteAiProvider(new(), new FakeKeys("secret-test-key"), http).StreamAsync("q", CancellationToken.None)) chunks.Add(chunk);
            Assert.Equal("SDK answer", chunks[0].Text); Assert.Contains(chunks, c => c.Text.Contains("length limit"));
        }
        Assert.Equal(2, calls); // disposing one SDK request did not dispose the shared/fake client
        Assert.Equal("https://api.anthropic.com/v1/messages", sentUrl); Assert.Equal("secret-test-key", sentKey);
        Assert.DoesNotContain("secret-test-key", sentBody); Assert.DoesNotContain("thinking", sentBody); Assert.DoesNotContain("temperature", sentBody);
        using var json = JsonDocument.Parse(sentBody!); Assert.Equal("low", json.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
    }
    [Fact] public async Task AnthropicAuthErrorsAreSanitizedAndRedirectsAreNeverFollowedBySdk()
    {
        foreach (int status in new[] { 401, 307 })
        {
            int calls = 0;
            using var http = new HttpClient(new FakeHandler((_, _) =>
            {
                calls++; var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"secret-test-key\"}}", Encoding.UTF8, "application/json") };
                response.Headers.Location = new Uri("https://do-not-follow.invalid/"); return Task.FromResult(response);
            }));
            var error = await Assert.ThrowsAsync<AiAnswerException>(async () => { await foreach (var _ in new PaletteAiProvider(new(), new FakeKeys("secret-test-key"), http).StreamAsync("q", CancellationToken.None)) { } });
            Assert.DoesNotContain("secret-test-key", error.ToString()); Assert.Null(error.InnerException); Assert.Equal(1, calls);
        }
    }
    [Fact] public async Task OversizedAnswerIsBoundedAndStopsTheProvider()
    {
        var session = new AiAnswerSession(); await session.AskAsync(new FixedProvider(new string('x', 64001)), "q");
        Assert.InRange(session.Answer.Length, 64000, 64100); Assert.Contains("truncated", session.Answer); Assert.False(session.Streaming);
    }
    [Fact] public async Task CancelIsImmediateEscAgainReturnsAndStaleChunksCannotReplaceNewAnswer()
    {
        var old = new ControlledProvider(); var session = new AiAnswerSession(); Task running = session.AskAsync(old, "old");
        Assert.True(session.Streaming); session.Cancel(); Assert.False(session.Streaming); Assert.True(session.Visible);
        await session.AskAsync(new FixedProvider("new answer"), "new");
        old.Completion.SetResult(new("stale")); await running;
        Assert.Equal("new answer", session.Answer); Assert.Equal("new", session.Question); session.Close(); Assert.False(session.Visible);
    }
    [Fact] public async Task ClosingCancelsTransportAndUnexpectedExceptionsBecomePlainStatus()
    {
        var provider = new ControlledProvider(); var session = new AiAnswerSession(); Task running = session.AskAsync(provider, "q");
        session.Close(); Assert.True(provider.Token.IsCancellationRequested); provider.Completion.SetException(new Exception("secret-test-key")); await running;
        Assert.False(session.Visible); Assert.DoesNotContain("secret-test-key", session.Status);
    }
    [Fact] public void MarkdownKeepsParagraphsListsInlineCodeAndCodeBlocksAsTextOnly()
    {
        var blocks = AiMarkdown.Parse("Hello `code`.\n\n- first\n- second\n```cs\n<unsafe>\n```\n");
        Assert.Equal(4, blocks.Count); Assert.Contains(blocks[0].Spans, s => s.Code && s.Text == "code");
        Assert.StartsWith("•", blocks[1].Spans[0].Text); Assert.True(blocks[^1].Code); Assert.Equal("<unsafe>", blocks[^1].Spans[0].Text);
    }
    [Fact] public async Task TimeoutIsInlineAndDoesNotCrashOrLeakTheQuestion()
    {
        var session = new AiAnswerSession(TimeSpan.FromMilliseconds(10));
        await session.AskAsync(new TimeoutProvider(), "private-question");
        Assert.False(session.Streaming); Assert.Contains("timed out", session.Status); Assert.DoesNotContain("private-question", session.Status);
    }
    [Theory] [InlineData("max_tokens", "length limit")] [InlineData("MaxTokens", "length limit")] [InlineData("refusal", "declined")]
    public void AnthropicStopReasonsProduceShortNotes(string reason, string note) => Assert.Contains(note, AiRules.StopNote(reason));
    [Theory] [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/chat")] [InlineData("https://api.deepseek.com/v1", "https://chat.deepseek.com/")]
    public void BrowserChatActionUsesTheChosenProviderWithoutTheQuestion(string endpoint, string expected) => Assert.Equal(expected, AiRules.ChatUrl(Local with { BaseUrl = endpoint }));
    private sealed class FakeKeys(string? value) : IAiCredentialStore
    { public string? Read(AiProviderKind _) => value; public void Save(AiProviderKind _, string key) => throw new NotSupportedException(); public void Remove(AiProviderKind _) => throw new NotSupportedException(); }
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private sealed class FixedProvider(string text) : IAiAnswerProvider
    {
        public int Calls;
        public async IAsyncEnumerable<AiChunk> StreamAsync(string question, [EnumeratorCancellation] CancellationToken token)
        { Calls++; await Task.Yield(); token.ThrowIfCancellationRequested(); yield return new(text); }
    }
    private sealed class ControlledProvider : IAiAnswerProvider
    {
        public CancellationToken Token;
        public TaskCompletionSource<AiChunk> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<AiChunk> StreamAsync(string question, [EnumeratorCancellation] CancellationToken token)
        { Token = token; yield return await Completion.Task; }
    }
    private sealed class TimeoutProvider : IAiAnswerProvider
    {
        public async IAsyncEnumerable<AiChunk> StreamAsync(string question, [EnumeratorCancellation] CancellationToken token)
        { await Task.Delay(Timeout.Infinite, token); yield return new("unreachable"); }
    }
}
