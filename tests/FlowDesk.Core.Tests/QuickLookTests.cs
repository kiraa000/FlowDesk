using System.Text;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.QuickLook;

namespace FlowDesk.Core.Tests;

public sealed class QuickLookTests
{
    private static QuickLookContext Valid => new(true, 10, "CabinetWClass", "DirectUIHWND", true, false, default, 0);
    [Theory]
    [InlineData("CabinetWClass", "DirectUIHWND", true, true)]
    [InlineData("Progman", "DirectUIHWND", true, true)]
    [InlineData("WorkerW", "DirectUIHWND", true, true)]
    [InlineData("Progman", "SysListView32", true, true)]
    [InlineData("WorkerW", "SysListView32", true, true)]
    [InlineData("CabinetWClass", "SysListView32", true, false)]
    [InlineData("WorkerW", "SysListView32", false, false)]
    [InlineData("Notepad", "DirectUIHWND", true, false)]
    [InlineData("CabinetWClass", "Edit", true, false)]
    [InlineData("CabinetWClass", "RichEditD2DPT", true, false)]
    [InlineData("CabinetWClass", "Microsoft.UI.Content.DesktopChildSiteBridge", true, false)]
    [InlineData("CabinetWClass", "DirectUIHWND", false, false)]
    [InlineData("WorkerW", "Edit", true, false)]
    public void FocusGate(string foreground, string focus, bool shell, bool accepted)
    {
        var result = new QuickLookTrigger().Observe(32, true, false, Valid with
        { ForegroundClass = foreground, FocusClass = focus, UnderShellView = shell });
        Assert.Equal(accepted, result.Swallow);
    }
    [Theory]
    [InlineData(true, false, false, false)] [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)] [InlineData(false, false, false, true)]
    public void AnyModifierPasses(bool win, bool alt, bool shift, bool control) =>
        Assert.False(new QuickLookTrigger().Observe(32, true, false, Valid with { Modifiers = new(win, alt, shift, control) }).Swallow);
    [Fact] public void DisabledInjectedAndImePass()
    {
        Assert.False(new QuickLookTrigger().Observe(32, true, false, Valid with { Enabled = false }).Swallow);
        Assert.False(new QuickLookTrigger().Observe(32, true, true, Valid).Swallow);
        Assert.False(new QuickLookTrigger().Observe(32, true, false, Valid with { ImeComposing = true }).Swallow);
    }
    [Theory] [InlineData(27, QuickLookCommand.Close)] [InlineData(13, QuickLookCommand.Open)]
    public void CloseOpenNeedMatchingOwner(int key, QuickLookCommand expected)
    {
        Assert.False(new QuickLookTrigger().Observe(key, true, false, Valid).Swallow);
        Assert.False(new QuickLookTrigger().Observe(key, true, false, Valid with { PreviewOwner = 11 }).Swallow);
        Assert.Equal(expected, new QuickLookTrigger().Observe(key, true, false, Valid with { PreviewOwner = 10 }).Command);
    }
    [Theory] [InlineData(32)] [InlineData(27)] [InlineData(13)]
    public void AcceptedDownOwnsRepeatAndUpAfterFocusOrToggleChanges(int key)
    {
        var trigger = new QuickLookTrigger();
        Assert.True(trigger.Observe(key, true, false, Valid with { PreviewOwner = 10 }).Swallow);
        var repeat = trigger.Observe(key, true, false, default);
        Assert.True(repeat.Swallow); Assert.Equal(QuickLookCommand.None, repeat.Command);
        Assert.True(trigger.Observe(key, false, false, default).Swallow);
        Assert.False(trigger.Observe(key, false, false, default).Swallow);
    }
    [Fact] public void RejectedDownDoesNotBecomeTriggerOnRepeat()
    {
        var trigger = new QuickLookTrigger();
        Assert.False(trigger.Observe(32, true, false, default).Swallow);
        Assert.False(trigger.Observe(32, true, false, Valid).Swallow);
        Assert.False(trigger.Observe(32, false, false, Valid).Swallow);
        Assert.Equal(QuickLookCommand.Toggle, trigger.Observe(32, true, false, Valid).Command);
    }
    [Fact] public void FlowDeskTaggedInputIsIgnoredButForeignInputCanTrigger()
    {
        var keyboard = new ShortcutKeyboardState();
        var trigger = new QuickLookTrigger();
        Assert.True(trigger.Observe(32, true, keyboard.Observe(32, true, 123).OwnInput, Valid).Swallow);
        Assert.False(trigger.Observe(32, false, keyboard.Observe(32, false, ShortcutKeyboardState.InjectionTag).OwnInput, Valid).Swallow);
        Assert.True(trigger.Observe(32, false, false, default).Swallow);
        Assert.False(new QuickLookTrigger().Observe(32, true, false, Valid with { Foreground = 0 }).Swallow);
        Assert.False(new QuickLookTrigger().Observe(65, true, false, Valid).Swallow);
    }
    [Theory]
    [InlineData("a.JPG", PreviewKind.Image)] [InlineData("a.avif", PreviewKind.Image)]
    [InlineData("a.pdf", PreviewKind.Pdf)] [InlineData("a.mp3", PreviewKind.Media)]
    [InlineData("a.mp4", PreviewKind.Media)] [InlineData("a.cs", PreviewKind.Text)]
    [InlineData("a.docx", PreviewKind.Fallback)]
    public void ExtensionChoice(string path, PreviewKind expected) => Assert.Equal(expected, PreviewContent.Choose(path, false, []));
    [Fact] public void SniffAndFolderChoice()
    {
        Assert.Equal(PreviewKind.Pdf, PreviewContent.Choose("a", false, "%PDF-1.7"u8));
        Assert.Equal(PreviewKind.Image, PreviewContent.Choose("a", false, "GIF89a"u8));
        Assert.Equal(PreviewKind.Text, PreviewContent.Choose("a", false, "hello world"u8));
        Assert.Equal(PreviewKind.Image, PreviewContent.Choose("a", false, "RIFF0000WEBP"u8));
        Assert.Equal(PreviewKind.Media, PreviewContent.Choose("a", false, "RIFF0000WAVE"u8));
        Assert.Equal(PreviewKind.Media, PreviewContent.Choose("a", false, "0000ftypmp42"u8));
        Assert.Equal(PreviewKind.Image, PreviewContent.Choose("a", false, "0000ftypavif"u8));
        Assert.Equal(PreviewKind.Fallback, PreviewContent.Choose("a.docx", false, "plain fake office"u8));
        Assert.Equal(PreviewKind.Fallback, PreviewContent.Choose("a", false, new byte[] { 0, 1, 2 }));
        Assert.Equal(PreviewKind.Folder, PreviewContent.Choose("a.pdf", true, []));
    }
    [Theory] [InlineData("utf-8")] [InlineData("utf-16")] [InlineData("utf-16BE")] [InlineData("utf-32")]
    public async Task DetectsBom(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes("Hello α😀")).ToArray();
        var result = await PreviewContent.ReadTextAsync(new MemoryStream(bytes), default);
        Assert.Equal("Hello α😀", result.Text); Assert.False(result.Truncated);
    }
    [Fact] public async Task TextCapDoesNotSplitUtf8Codepoint()
    {
        byte[] bytes = Enumerable.Repeat((byte)'a', PreviewContent.TextLimit - 1).Concat(Encoding.UTF8.GetBytes("😀more")).ToArray();
        var result = await PreviewContent.ReadTextAsync(new MemoryStream(bytes), default);
        Assert.True(result.Truncated); Assert.Equal(PreviewContent.TextLimit - 1, result.Text.Length);
        Assert.DoesNotContain('\uFFFD', result.Text);
    }
    [Fact] public async Task ExactCapAndCancellation()
    {
        var result = await PreviewContent.ReadTextAsync(new MemoryStream(new byte[PreviewContent.TextLimit]), default);
        Assert.False(result.Truncated);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PreviewContent.ReadTextAsync(new MemoryStream([1]), new CancellationToken(true)));
    }
    [Fact] public async Task BomlessUtf16AndLegacyAnsi()
    {
        var utf16 = await PreviewContent.ReadTextAsync(new MemoryStream(Encoding.Unicode.GetBytes("Hello world")), default);
        Assert.Equal("Hello world", utf16.Text);
        var ansi = await PreviewContent.ReadTextAsync(new MemoryStream(new byte[] { 99, 97, 102, 233 }), default);
        Assert.Equal("café", ansi.Text);
    }
    [Fact] public void StaleGenerationCannotPublish()
    {
        var generations = new PreviewGeneration();
        long old = generations.Next(), latest = generations.Next();
        Assert.False(generations.IsCurrent(old)); Assert.True(generations.IsCurrent(latest));
        generations.Next(); Assert.False(generations.IsCurrent(latest));
    }
    [Theory]
    [InlineData(100000, 1, 2048, 1)] [InlineData(1, 100000, 1, 2048)]
    [InlineData(4000, 2000, 2048, 1024)] [InlineData(300, 200, 300, 200)]
    [InlineData(-1, 200, 1, 1)]
    public void DecodeIsBoundedInBothAxes(double width, double height, uint expectedWidth, uint expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), PreviewContent.Fit(width, height, 2048));
}
