using FlowDesk.Core.Palette;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class ClipboardTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    [Fact] public void OptInExactDedupeNewestAndOffClears()
    {
        var h = new ClipboardHistory();
        Assert.False(h.Capture("text", new(), false, Now));
        h.SetEnabled(true);
        Assert.True(h.Capture(" text \r\n", new(), false, Now));
        h.Capture("other", new(), false, Now);
        h.Capture(" text \r\n", new(), false, Now.AddMinutes(1));
        Assert.Equal(2, h.Snapshot().Length);
        Assert.Equal(" text \r\n", h.Snapshot()[0].Text);
        Assert.Equal("text", h.Snapshot()[0].Display);
        h.SetEnabled(false); Assert.Empty(h.Snapshot());
        h.SetEnabled(true); Assert.Empty(h.Snapshot());
    }
    [Fact] public void LimitsWhitespaceAndClear()
    {
        var h = new ClipboardHistory(); h.SetEnabled(true);
        Assert.False(h.Capture(" \t\n", new(), false, Now));
        Assert.False(h.Capture(new string('x', ClipboardHistory.MaxBytes / 2 + 1), new(), false, Now));
        Assert.True(h.Capture(new string('x', ClipboardHistory.MaxBytes / 2), new(), false, Now));
        for (int i = 0; i < 51; i++) h.Capture(i.ToString(), new(), false, Now);
        Assert.Equal(50, h.Snapshot().Length); Assert.Equal("50", h.Snapshot()[0].Text);
        Assert.Equal("1", h.Snapshot()[^1].Text); h.Clear(); Assert.Empty(h.Snapshot());
    }
    [Fact] public void PrivacyAndSelfWritesFailClosed()
    {
        var h = new ClipboardHistory(); h.SetEnabled(true);
        foreach (var p in new ClipboardPrivacy[] { new(Excluded: true), new(IncludeHistory: 0), new(UploadCloud: 0), new(ReadFailed: true) })
            Assert.False(h.Capture("secret", p, false, Now));
        Assert.False(h.Capture("ours", new(), true, Now)); Assert.Empty(h.Snapshot());
        Assert.True(h.Capture("public", new(IncludeHistory: 1, UploadCloud: 1), false, Now));
    }
    [Fact] public void FilterHasNoJunkSubsequenceAndModeRules()
    {
        var entry = new ClipboardEntry(1, "alpha banana gamma", Now);
        Assert.True(ClipboardHistory.Matches(entry, "banana")); Assert.False(ClipboardHistory.Matches(entry, "abgm"));
        Assert.Equal(PaletteMode.Commands, ClipboardModeRules.Resolve(PaletteMode.Clipboard, false));
        Assert.Equal(PaletteMode.Commands, ClipboardModeRules.Backspace(PaletteMode.Clipboard, ""));
        Assert.Equal(PaletteMode.Clipboard, ClipboardModeRules.Backspace(PaletteMode.Clipboard, "x"));
    }
    [Theory]
    [InlineData(12,12,true,false,true,false,true)]
    [InlineData(12,13,true,false,true,false,false)]
    [InlineData(12,12,false,false,true,false,false)]
    [InlineData(12,12,true,true,true,false,false)]
    [InlineData(12,12,true,false,false,false,false)]
    [InlineData(12,12,true,false,true,true,false)]
    [InlineData(1,1,true,false,true,false,false)]
    public void PasteRequiresSameLiveUnelevatedFocusedTarget(uint captured, uint current, bool exists, bool elevated, bool focused, bool modifiers, bool expected) =>
        Assert.Equal(expected, ClipboardPasteRules.CanPaste(captured, current, 1, exists, elevated, focused, modifiers));
    [Fact] public void CatalogBindingAndIndependentPolicy()
    {
        var b = HotkeyCatalog.Find(HotkeyCatalog.ClipboardId)!;
        Assert.Equal(HotkeyCatalog.ModWin | HotkeyCatalog.ModAlt, b.Modifiers); Assert.Equal(0x56u, b.VirtualKey);
        Assert.False(b.Repeat); Assert.False(b.Pausable);
        Assert.True(new ShortcutPolicy([]).ForModules(false, false, true).Enabled(b.Id));
        Assert.False(new ShortcutPolicy([]).ForModules(true, true).Enabled(b.Id));
    }
}
