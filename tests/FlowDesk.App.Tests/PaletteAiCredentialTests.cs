using FlowDesk.App.Services;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.Tests;

public sealed class PaletteAiCredentialTests
{
    [Fact] public void WrapperUsesProviderTargetsAndZerosTransientBuffersWithoutPrintingSecrets()
    {
        var backend = new FakeBackend(); var store = new AiCredentialStore(backend);
        store.Save(AiProviderKind.Anthropic, "fake-test-key-1234");
        Assert.Equal("FlowDesk/ai/Anthropic", backend.Target); Assert.All(backend.WriteBuffer!, b => Assert.Equal(0, b));
        Assert.Equal("fake-test-key-1234", store.Read(AiProviderKind.Anthropic)); Assert.All(backend.ReadBuffer!, b => Assert.Equal(0, b));
        Assert.DoesNotContain("fake-test-key", store.ToString()); store.Remove(AiProviderKind.Anthropic); Assert.Null(store.Read(AiProviderKind.Anthropic));
        Assert.NotEqual(AiCredentialStore.Target(AiProviderKind.Anthropic), AiCredentialStore.Target(AiProviderKind.OpenAICompatible));
    }
    [Fact] public void OldSettingsHaveNoKeyFieldsAndInvalidKeysAreNotEchoedInErrors()
    {
        var settings = AppSettings.FromJson("{}"); Assert.Equal("claude-opus-5-5", settings.Ai.Model);
        string json = settings.ToJson(); Assert.DoesNotContain("ApiKey", json); Assert.DoesNotContain("Credential", json);
        var error = Assert.Throws<AiAnswerException>(() => new AiCredentialStore(new FakeBackend()).Save(AiProviderKind.Anthropic, "fake-test-key\n"));
        Assert.DoesNotContain("fake-test-key", error.ToString());
    }
    [Fact] public void SettingsUsePasswordBoxNeverRedisplaySavedKeyAndTestRequiresEnter()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "AppSources", "Views");
        string markup = File.ReadAllText(Path.Combine(root, "AiSettingsPanel.xaml"));
        Assert.Contains("<PasswordBox x:Name=\"KeyBox\"", markup);
        Assert.Contains("Your question is sent to the provider you choose only when you press Enter.", markup);
        string source = File.ReadAllText(Path.Combine(root, "AiSettingsPanel.xaml.cs"));
        Assert.DoesNotContain("KeyBox.Password = key", source); Assert.Contains("KeyBox.Password = \"\"", source);
        Assert.Contains("if (!enter) args.Cancel = true", source);
        string window = File.ReadAllText(Path.Combine(root, "CommandPaletteWindow.Ai.cs"));
        Assert.Contains("if (answer.Streaming) answer.Cancel(); else ReturnFromAnswer()", window);
        Assert.Contains("SearchBox.IsReadOnly = visible", window);
        Assert.Contains("_ = LoadAnswerAsync(provider, question)", window);
        Assert.Contains("TimeSpan.FromMilliseconds(40)", window);
        Assert.Contains("AnswerBody.Focus(FocusState.Programmatic)", window);
        Assert.DoesNotContain("WebView", markup);
    }
    [Fact] public void NativeBlobLimitAndMalformedCredentialsAreRejectedWithoutEchoingData()
    {
        var backend = new FakeBackend(); var store = new AiCredentialStore(backend);
        store.Save(AiProviderKind.Anthropic, new string('x', 1280)); Assert.NotNull(store.Read(AiProviderKind.Anthropic));
        Assert.Throws<AiAnswerException>(() => store.Save(AiProviderKind.Anthropic, new string('x', 1281)));
        backend.Write("fake", [65]); Assert.Throws<AiAnswerException>(() => store.Read(AiProviderKind.Anthropic));
        backend.Write("fake", new byte[2562]); Assert.Throws<AiAnswerException>(() => store.Read(AiProviderKind.Anthropic));
    }
    private sealed class FakeBackend : IAiCredentialBackend
    {
        private byte[]? stored;
        public byte[]? WriteBuffer, ReadBuffer;
        public string? Target;
        public byte[]? Read(string target) { Target = target; return ReadBuffer = stored?.ToArray(); }
        public void Write(string target, byte[] value) { Target = target; WriteBuffer = value; stored = value.ToArray(); }
        public void Delete(string target) { Target = target; stored = null; }
    }
}
